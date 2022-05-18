namespace TheCurator.Logic.Features;

public class Audio :
    SyncDisposable,
    IFeature
{
    public Audio()
    {
        playedIndexes = new();
        playlist = new();
        playerAccess = new();
        seekCommand = new();
        shufflePlayedIndexes = new();
        streamingThrottle = new(true);
        RequestIdentifiers = new string[] { "audio", "voice" };
    }

    IAudioClient? audioClient;
    bool isShuffling;
    CancellationTokenSource? playerCancellationTokenSource;
    readonly List<int> playedIndexes;
    readonly AsyncLock playerAccess;
    readonly List<FileInfo> playlist;
    RepeatMode repeatMode;
    readonly ConcurrentQueue<TimeSpan> seekCommand;
    readonly List<int> shufflePlayedIndexes;
    readonly AsyncManualResetEvent streamingThrottle;

    public string Description =>
        "Allows The Curator to interact with voice channels";

    public IReadOnlyList<(string command, string description)> Examples =>
        new (string command, string description)[]
        {
            ("join", "The Curator joins your current voice channel"),
            ("leave", "The Curator leaves whatever voice channel it is currently in"),
            ("play [service?] [content]", "Adds something to the playlist from a service"),
            ("pause", "Pauses playback or resumes it if it is already paused"),
            ("repeat", "Change the repeat mode"),
            ("resume", "Resumes playback if it is paused"),
            ("seek [position]", "Seek to a position in time in the current track"),
            ("shuffle", "Toggle shuffling"),
            ("stop", "Stops playing any audio that it is currently playing")
        };

    public string Name =>
        "Audio";

    public IReadOnlyList<string> RequestIdentifiers { get; }

    async Task AddPlaylistItemAsync(FileInfo fileInfo, Func<IVoiceChannel> getVoiceChannel)
    {
        using (await playerAccess.LockAsync().ConfigureAwait(false))
            playlist.Add(fileInfo);
        await PlayAsync(getVoiceChannel).ConfigureAwait(false);
    }

    async Task AddYouTubePlaylistPlaylistItemsAsync(string youtubeId, Func<IVoiceChannel> getVoiceChannel)
    {
        var youtube = new YoutubeClient();
        await foreach (var video in youtube.Playlists.GetVideosAsync(youtubeId))
            await AddYouTubeVideoPlaylistItemAsync(video.Id.Value, getVoiceChannel).ConfigureAwait(false);
    }

    async Task AddYouTubeVideoPlaylistItemAsync(string youtubeId, Func<IVoiceChannel> getVoiceChannel)
    {
        var appDirectoryInfo = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        var cacheDirectoryInfo = new DirectoryInfo(Path.Combine(appDirectoryInfo.FullName, "Cache"));
        if (!cacheDirectoryInfo.Exists)
            cacheDirectoryInfo.Create();
        var youtubeCacheDirectoryInfo = new DirectoryInfo(Path.Combine(cacheDirectoryInfo.FullName, "YouTube"));
        if (!youtubeCacheDirectoryInfo.Exists)
            youtubeCacheDirectoryInfo.Create();
        var cacheFileInfo = new FileInfo(Path.Combine(youtubeCacheDirectoryInfo.FullName, youtubeId));
        if (!cacheFileInfo.Exists)
        {
            var youtube = new YoutubeClient();
            await youtube.Videos.Streams.DownloadAsync((await youtube.Videos.Streams.GetManifestAsync(youtubeId).ConfigureAwait(false)).GetAudioOnlyStreams().GetWithHighestBitrate(), cacheFileInfo.FullName).ConfigureAwait(false);
        }
        await AddPlaylistItemAsync(cacheFileInfo, getVoiceChannel).ConfigureAwait(false);
    }

    async Task ConnectAsync(IVoiceChannel voiceChannel)
    {
        if (audioClient is not null)
            throw new Exception("Already connected");
        audioClient = await voiceChannel.ConnectAsync(true).ConfigureAwait(false);
    }

    async Task DisconnectAsync()
    {
        await StopAsync().ConfigureAwait(false);
        if (audioClient is not null)
        {
            audioClient.Dispose();
            audioClient = null;
        }
    }

    protected override bool Dispose(bool disposing)
    {
        if (disposing)
            DisconnectAsync().Wait();
        return true;
    }

    async Task PlayAsync(Func<IVoiceChannel> getVoiceChannel)
    {
        if (audioClient is null)
            await ConnectAsync(getVoiceChannel()).ConfigureAwait(false);
        using (await playerAccess.LockAsync().ConfigureAwait(false))
        {
            if (playerCancellationTokenSource is not null)
                return;
            playerCancellationTokenSource = new();
            _ = Task.Run(PlayerLogicAsync);
        }
    }

    async Task PlayerLogicAsync()
    {
        seekCommand.Clear();
        while (true)
        {
            FileInfo? fileInfo = null;
            using (await playerAccess.LockAsync().ConfigureAwait(false))
            {
                var playlistIndex = playedIndexes.LastOrDefault();
                if (repeatMode != RepeatMode.Single)
                {
                    if (isShuffling)
                    {
                        var shuffle = true;
                        if (shufflePlayedIndexes.Count == playlist.Count)
                        {
                            shufflePlayedIndexes.Clear();
                            if (repeatMode == RepeatMode.None)
                                shuffle = false;
                        }
                        if (shuffle)
                        {
                            var possibleIndexes = Enumerable
                                .Range(0, playlist.Count)
                                .Except(shufflePlayedIndexes)
                                .ToImmutableArray();
                            playlistIndex = possibleIndexes[new Random().Next(0, possibleIndexes.Length)];
                            shufflePlayedIndexes.Add(playlistIndex);
                        }
                        else
                            playlistIndex = -1;
                    }
                    else if (playedIndexes.Count > 0)
                    {
                        ++playlistIndex;
                        if (playlistIndex == playlist.Count && repeatMode == RepeatMode.Playlist)
                            playlistIndex = 0;
                    }
                }
                if (playlistIndex >= 0 && playlistIndex < playlist.Count)
                {
                    fileInfo = playlist[playlistIndex];
                    playedIndexes.Add(playlistIndex);
                }
            }
            try
            {
                if (fileInfo is not null && audioClient is not null)
                {
                    var cancellationToken = playerCancellationTokenSource?.Token ?? throw new OperationCanceledException();
                    while (true)
                    {
                        using var ffmpegInstance = seekCommand.TryDequeue(out var seekTo) ? CreateFfmpegInstance(fileInfo.FullName, seekTo) : CreateFfmpegInstance(fileInfo.FullName);
                        using var ffmpegOutputStream = ffmpegInstance.StandardOutput.BaseStream;
                        using var discordInputStream = audioClient.CreatePCMStream(AudioApplication.Music);
                        try
                        {
                            var bytesRead = -1;
                            while (bytesRead != 0 && ffmpegOutputStream.CanRead)
                            {
                                await streamingThrottle.WaitAsync().ConfigureAwait(false);
                                if (seekCommand.TryPeek(out _))
                                    break;
                                var buffer = new byte[bufferSize];
                                bytesRead = await ffmpegOutputStream.ReadAsync(buffer, 0, bufferSize).ConfigureAwait(false);
                                using var bufferStream = new MemoryStream(buffer);
                                await bufferStream.CopyToAsync(discordInputStream, cancellationToken).ConfigureAwait(false);
                            }
                        }
                        finally
                        {
                            await discordInputStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                        }
                        if (seekCommand.TryPeek(out _))
                            continue;
                        break;
                    }
                }
                else
                    break;
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
        await StopAsync().ConfigureAwait(false);
    }

    async Task StopAsync()
    {
        using (await playerAccess.LockAsync().ConfigureAwait(false))
        {
            if (playerCancellationTokenSource is not null)
            {
                playerCancellationTokenSource.Cancel();
                playerCancellationTokenSource.Dispose();
                playerCancellationTokenSource = null;
            }
            playlist.Clear();
            playedIndexes.Clear();
            shufflePlayedIndexes.Clear();
        }
    }

    public async Task<bool> ProcessRequestAsync(SocketMessage message, IReadOnlyList<string> commandArgs)
    {
        if (commandArgs.Count >= 2 &&
            RequestIdentifiers.Contains(commandArgs[0], StringComparer.OrdinalIgnoreCase) &&
            commandArgs[1] is { } command &&
            !string.IsNullOrWhiteSpace(command))
        {
            if (commandArgs.Count == 2)
            {
                if (command.Equals("join", StringComparison.OrdinalIgnoreCase))
                {
                    if (message.Author is IGuildUser guildUser)
                    {
                        if (audioClient is not null)
                            await DisconnectAsync().ConfigureAwait(false);
                        if (guildUser.VoiceChannel is { } voiceChannel)
                        {
                            await ConnectAsync(voiceChannel).ConfigureAwait(false);
                            await message.Channel.SendMessageAsync("I have joined your channel.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                            return true;
                        }
                        else
                            throw new Exception("Command author is not in a voice channel on the server");
                    }
                }
                if (command.Equals("leave", StringComparison.OrdinalIgnoreCase))
                {
                    if (audioClient is not null)
                    {
                        await DisconnectAsync().ConfigureAwait(false);
                        await message.Channel.SendMessageAsync("I have left the channel I was in.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                    }
                    else
                        await message.Channel.SendMessageAsync("I am not currently in a channel.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                    return true;
                }
                if (command.Equals("pause", StringComparison.OrdinalIgnoreCase))
                {
                    if (streamingThrottle.IsSet)
                    {
                        streamingThrottle.Reset();
                        await message.Channel.SendMessageAsync("Playback paused.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                    }
                    else
                    {
                        streamingThrottle.Set();
                        await message.Channel.SendMessageAsync("Playback resumed.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                    }
                    return true;
                }
                if (command.Equals("repeat", StringComparison.OrdinalIgnoreCase))
                {
                    repeatMode = (RepeatMode)(((int)repeatMode + 1) % 3);
                    switch (repeatMode)
                    {
                        case RepeatMode.Playlist:
                            await message.Channel.SendMessageAsync("Repeating the playlist.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                            break;
                        case RepeatMode.Single:
                            await message.Channel.SendMessageAsync("Repeating the current track.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                            break;
                        default:
                            await message.Channel.SendMessageAsync("Repeating disabled.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                            break;
                    }
                    return true;
                }
                if (command.Equals("resume", StringComparison.OrdinalIgnoreCase))
                {
                    streamingThrottle.Set();
                    await message.Channel.SendMessageAsync("Playback resumed.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                    return true;
                }
                if (command.Equals("shuffle", StringComparison.OrdinalIgnoreCase))
                {
                    isShuffling = !isShuffling;
                    await message.Channel.SendMessageAsync(isShuffling ? "Shuffling." : "Not shuffling.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                    return true;
                }
                if (command.Equals("stop", StringComparison.OrdinalIgnoreCase))
                {
                    await StopAsync().ConfigureAwait(false);
                    await message.Channel.SendMessageAsync("Playback stopped.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                    return true;
                }
            }
            if (commandArgs.Count == 3 && command.Equals("seek", StringComparison.OrdinalIgnoreCase))
            {
                var isPlaying = false;
                using (await playerAccess.LockAsync().ConfigureAwait(false))
                    isPlaying = playerCancellationTokenSource is not null;
                if (isPlaying)
                {
                    var positionText = commandArgs[2];
                    TimeSpan? position = null;
                    if (TimeSpan.TryParse($"00:{positionText}", out var parsedTs))
                        position = parsedTs;
                    else if (TimeSpan.TryParse(positionText, out parsedTs))
                        position = parsedTs;
                    else if (double.TryParse(positionText, out var parsedD))
                        position = TimeSpan.FromSeconds(parsedD);
                    if (position is { } nonNullPosition)
                    {
                        seekCommand.Enqueue(nonNullPosition);
                        await message.Channel.SendMessageAsync($"Seeking to: {nonNullPosition}.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                        return true;
                    }
                    else
                        throw new Exception("Cannot comprehend specified position");
                }
                else
                {
                    await message.Channel.SendMessageAsync("Nothing is playing.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                    return true;
                }
            }
            if (commandArgs.Count >= 3 && command.Equals("play", StringComparison.OrdinalIgnoreCase))
            {
                IVoiceChannel getVoiceChannel() =>
                    ((IGuildUser)message.Author).VoiceChannel ?? throw new Exception("Command author is not in a voice channel on the server");
                if (commandArgs.Count >= 4)
                {
                    var service = commandArgs[2];
                    if (!string.IsNullOrWhiteSpace(service))
                    {
                        if (service.Equals("local", StringComparison.OrdinalIgnoreCase) &&
                            commandArgs[3] is { } path &&
                            new FileInfo(path) is { } fileInfo)
                        {
                            if (fileInfo.Exists)
                            {
                                await AddPlaylistItemAsync(fileInfo, getVoiceChannel).ConfigureAwait(false);
                                await message.Channel.SendMessageAsync("The local file was added to the playlist.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                                return true;
                            }
                            throw new FileNotFoundException();
                        }
                        if (service.Equals("youtube", StringComparison.OrdinalIgnoreCase) ||
                            service.Equals("yt", StringComparison.OrdinalIgnoreCase))
                        {
                            var playlistIdMatch = Regex.Match(string.Join(" ", commandArgs.Skip(3)), @"[0-9a-zA-Z_]{34}");
                            if (playlistIdMatch.Success)
                            {
                                await AddYouTubePlaylistPlaylistItemsAsync(playlistIdMatch.Value, getVoiceChannel).ConfigureAwait(false);
                                await message.Channel.SendMessageAsync("The videos in the YouTube playlist were added to my playlist.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                                return true;
                            }
                            var videoIdMatch = Regex.Match(string.Join(" ", commandArgs.Skip(3)), @"[0-9a-zA-Z_]{11}");
                            if (videoIdMatch.Success)
                            {
                                await AddYouTubeVideoPlaylistItemAsync(videoIdMatch.Value, getVoiceChannel).ConfigureAwait(false);
                                await message.Channel.SendMessageAsync("The YouTube video was added to the playlist.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                                return true;
                            }
                            await foreach (var result in new YoutubeClient().Search.GetResultsAsync(string.Join(" ", commandArgs.Skip(3))))
                                if (result is VideoSearchResult video)
                                {
                                    await AddYouTubeVideoPlaylistItemAsync(video.Id.Value, getVoiceChannel).ConfigureAwait(false);
                                    await message.Channel.SendMessageAsync($"This YouTube video was added to the playlist: https://youtu.be/{video.Id.Value}", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                                    return true;
                                }
                            throw new Exception("YouTube media not found");
                        }
                    }
                }
                {
                    var playlistIdMatch = Regex.Match(string.Join(" ", commandArgs.Skip(2)), @"[0-9a-zA-Z_]{34}");
                    if (playlistIdMatch.Success)
                    {
                        await AddYouTubePlaylistPlaylistItemsAsync(playlistIdMatch.Value, getVoiceChannel).ConfigureAwait(false);
                        await message.Channel.SendMessageAsync("The videos in the YouTube playlist were added to my playlist.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                        return true;
                    }
                    var youtubeVideoIdMatch = Regex.Match(string.Join(" ", commandArgs.Skip(2)), @"[0-9a-zA-Z_]{11}");
                    if (youtubeVideoIdMatch.Success)
                    {
                        await AddYouTubeVideoPlaylistItemAsync(youtubeVideoIdMatch.Value, getVoiceChannel).ConfigureAwait(false);
                        await message.Channel.SendMessageAsync("The YouTube video was added to the playlist.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                        return true;
                    }
                    if (commandArgs[2] is { } path &&
                        !string.IsNullOrWhiteSpace(path) &&
                        new FileInfo(path) is { } fileInfo &&
                        fileInfo.Exists)
                    {
                        await AddPlaylistItemAsync(fileInfo, getVoiceChannel).ConfigureAwait(false);
                        await message.Channel.SendMessageAsync("The local file was added to the playlist.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                        return true;
                    }
                    await foreach (var result in new YoutubeClient().Search.GetResultsAsync(string.Join(" ", commandArgs.Skip(2))))
                        if (result is VideoSearchResult video)
                        {
                            await AddYouTubeVideoPlaylistItemAsync(video.Id.Value, getVoiceChannel).ConfigureAwait(false);
                            await message.Channel.SendMessageAsync($"This YouTube video was added to the playlist: https://youtu.be/{video.Id.Value}", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                            return true;
                        }
                    throw new Exception("Media not found");
                }
            }
        }
        return false;
    }

    const int bufferSize = 4096;

    static Process CreateFfmpegInstance(string path, TimeSpan? seekTo = null) =>
        Process.Start(new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = $"-hide_banner -loglevel panic {(seekTo is { } nonNullSeekTo ? $"-ss {nonNullSeekTo.Days * 24 + nonNullSeekTo.Hours:00}:{nonNullSeekTo.Minutes:00}:{nonNullSeekTo.Seconds:00} " : "")}-i \"{path}\" -ac 2 -f s16le -ar 48000 pipe:1",
            UseShellExecute = false,
            RedirectStandardOutput = true,
        });
}
