namespace TheCurator.Logic.Features;

public class Audio :
    SyncDisposable,
    IFeature
{
    public Audio()
    {
        decibelAdjust = -12;
        isLoudnessNormalized = true;
        playedIndexes = new();
        playlist = new();
        playerAccess = new();
        seekCommand = new();
        skipCommand = new();
        shufflePlayedIndexes = new();
        streamingThrottle = new(true);
        RequestIdentifiers = new string[] { "audio", "voice" };
    }

    IAudioClient? audioClient;
    IVoiceChannel? connectedVoiceChannel;
    double decibelAdjust;
    bool isDisconnecting;
    bool isLoudnessNormalized;
    bool isShuffling;
    CancellationTokenSource? playerCancellationTokenSource;
    readonly List<int> playedIndexes;
    readonly AsyncLock playerAccess;
    readonly List<FileInfo> playlist;
    RepeatMode repeatMode;
    readonly ConcurrentQueue<TimeSpan> seekCommand;
    readonly ConcurrentQueue<bool> skipCommand;
    readonly List<int> shufflePlayedIndexes;
    readonly AsyncManualResetEvent streamingThrottle;

    public string Description =>
        "Allows The Curator to interact with voice channels";

    public IReadOnlyList<(string command, string description)> Examples =>
        new (string command, string description)[]
        {
            ("back, prev, previous", "Move to the previous track"),
            ("decibeladjust [delta?]", "Specify the positive or negative amount of decibels by which to adjust the audio volume"),
            ("join", "The Curator joins your current voice channel"),
            ("leave", "The Curator leaves whatever voice channel it is currently in"),
            ("normalize", "Toggle loudness normalization"),
            ("pause", "Pauses playback or resumes it if it is already paused"),
            ("play [service?] [content]", "Adds something to the playlist from a service"),
            ("repeat", "Change the repeat mode"),
            ("resume", "Resumes playback if it is paused"),
            ("say [text], speak [text]", "Convert text to speech and play it"),
            ("seek [position]", "Seek to a position in time in the current track"),
            ("shuffle", "Toggle shuffling"),
            ("skip, next", "Move to the next track"),
            ("stop", "Stops playing any audio that it is currently playing")
        };

    public string Name =>
        "Audio";

    public IReadOnlyList<string> RequestIdentifiers { get; }

    async Task AddPlaylistItemAsync(FileInfo fileInfo, IVoiceChannel voiceChannel)
    {
        using (await playerAccess.LockAsync().ConfigureAwait(false))
            playlist.Add(fileInfo);
        await PlayAsync(voiceChannel).ConfigureAwait(false);
    }

    async Task AddYouTubePlaylistPlaylistItemsAsync(string youtubeId, IVoiceChannel voiceChannel)
    {
        var youtube = new YoutubeClient();
        await foreach (var video in youtube.Playlists.GetVideosAsync(youtubeId))
            await AddYouTubeVideoPlaylistItemAsync(video.Id.Value, voiceChannel).ConfigureAwait(false);
    }

    async Task AddYouTubeVideoPlaylistItemAsync(string youtubeId, IVoiceChannel voiceChannel)
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
        await AddPlaylistItemAsync(cacheFileInfo, voiceChannel).ConfigureAwait(false);
    }

    async Task ConnectAsync(IVoiceChannel voiceChannel)
    {
        if (audioClient is not null)
            throw new Exception("Already connected");
        audioClient = await voiceChannel.ConnectAsync(true).ConfigureAwait(false);
        connectedVoiceChannel = voiceChannel;
    }

    async Task DisconnectAsync()
    {
        isDisconnecting = true;
        if (!await StopAsync().ConfigureAwait(false) && connectedVoiceChannel is not null)
            await PlayAsync(connectedVoiceChannel).ConfigureAwait(false);
        if (audioClient is not null)
        {
            await audioClient.StopAsync().ConfigureAwait(false);
            audioClient.Dispose();
            audioClient = null;
            connectedVoiceChannel = null;
        }
        isDisconnecting = false;
    }

    protected override bool Dispose(bool disposing)
    {
        if (disposing)
            DisconnectAsync().Wait();
        return true;
    }

    async Task PlayAsync(IVoiceChannel voiceChannel)
    {
        var announceEntrance = false;
        if (audioClient is null)
        {
            await ConnectAsync(voiceChannel).ConfigureAwait(false);
            announceEntrance = true;
        }
        using (await playerAccess.LockAsync().ConfigureAwait(false))
        {
            if (playerCancellationTokenSource is not null)
                return;
            playerCancellationTokenSource = new();
            _ = Task.Run(() => PlayerLogicAsync(announceEntrance));
        }
    }

    async Task PlayAudioAsync(AudioOutStream discordInputStream, FileInfo fileInfo, bool isSkippable)
    {
        var cancellationToken = playerCancellationTokenSource?.Token ?? CancellationToken.None;
        var playTime = new Stopwatch();
        while (true)
        {
            using var ffmpegInstance = seekCommand.TryDequeue(out var seekTo) ? CreateFfmpegInstance(fileInfo.FullName, isLoudnessNormalized, decibelAdjust, seekTo < TimeSpan.Zero ? playTime.Elapsed : seekTo) : CreateFfmpegInstance(fileInfo.FullName, isLoudnessNormalized, decibelAdjust);
            using var ffmpegOutputStream = ffmpegInstance.StandardOutput.BaseStream;
            if (!playTime.IsRunning)
                playTime.Start();
            var bytesRead = -1;
            while (bytesRead != 0 && ffmpegOutputStream.CanRead)
            {
                await streamingThrottle.WaitAsync().ConfigureAwait(false);
                if (seekCommand.TryPeek(out _) || (isSkippable && skipCommand.TryPeek(out _)))
                    break;
                var buffer = new byte[bufferSize];
                bytesRead = await ffmpegOutputStream.ReadAsync(buffer, 0, bufferSize).ConfigureAwait(false);
                using var bufferStream = new MemoryStream(buffer);
                await bufferStream.CopyToAsync(discordInputStream, cancellationToken).ConfigureAwait(false);
            }
            if (seekCommand.TryPeek(out _))
                continue;
            break;
        }
    }

    async Task PlayerLogicAsync(bool announceEntrance)
    {
        if (audioClient is null)
            return;
        using var discordInputStream = audioClient.CreatePCMStream(AudioApplication.Mixed);
        var appDirectoryInfo = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        var resourcesDirectoryInfo = new DirectoryInfo(Path.Combine(appDirectoryInfo.FullName, "Resources"));
        if (announceEntrance && resourcesDirectoryInfo.Exists)
        {
            var entranceSoundFileInfo = new FileInfo(Path.Combine(resourcesDirectoryInfo.FullName, "546638.mp3"));
            if (entranceSoundFileInfo.Exists)
                await PlayAudioAsync(discordInputStream, entranceSoundFileInfo, false).ConfigureAwait(false);
        }
        try
        {
            while (true)
            {
                seekCommand.Clear();
                FileInfo? fileInfo = null;
                var playedIndexesCount = 0;
                using (await playerAccess.LockAsync().ConfigureAwait(false))
                {
                    var playlistIndex = playedIndexes.LastOrDefault();
                    if (skipCommand.TryDequeue(out var skip) && !skip)
                    {
                        playedIndexes.RemoveAt(playedIndexes.Count - 1);
                        playlistIndex = playedIndexes.LastOrDefault();
                    }
                    else if (repeatMode != RepeatMode.Single)
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
                        if (playedIndexes.Count == 0 || playedIndexes.LastOrDefault() != playlistIndex)
                            playedIndexes.Add(playlistIndex);
                    }
                    playedIndexesCount = playedIndexes.Count;
                }
                try
                {
                    if (fileInfo is not null)
                    {
                        var getPSAFileInfoTask = Task.Run(() =>
                        {
                            var settings = Settings.Instance;
                            if (settings.AudioPSAs is { } psas &&
                                psas.Length > 0 &&
                                settings.AudioPSAFrequency is { } psaFrequency &&
                                psaFrequency > 0 &&
                                playedIndexesCount % psaFrequency == 0)
                                return SynthesizeSpeech(psas[new Random().Next(0, psas.Length)]);
                            return null;
                        });
                        await PlayAudioAsync(discordInputStream, fileInfo, true).ConfigureAwait(false);
                        if (await getPSAFileInfoTask.ConfigureAwait(false) is { } psaFileInfo)
                            await PlayAudioAsync(discordInputStream, psaFileInfo, false).ConfigureAwait(false);
                    }
                    else
                        break;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
            if (resourcesDirectoryInfo.Exists)
            {
                var exitSoundFile = new FileInfo(Path.Combine(resourcesDirectoryInfo.FullName, "546641.mp3"));
                if (exitSoundFile.Exists)
                    await PlayAudioAsync(discordInputStream, exitSoundFile, false).ConfigureAwait(false);
            }
        }
        finally
        {
            await discordInputStream.FlushAsync().ConfigureAwait(false);
        }
        if (!isDisconnecting)
            await DisconnectAsync().ConfigureAwait(false);
    }

    async Task<bool> StopAsync()
    {
        var interruptedSomething = false;
        using (await playerAccess.LockAsync().ConfigureAwait(false))
        {
            if (playerCancellationTokenSource is not null)
            {
                interruptedSomething = true;
                playerCancellationTokenSource.Cancel();
                playerCancellationTokenSource.Dispose();
                playerCancellationTokenSource = null;
            }
            playlist.Clear();
            playedIndexes.Clear();
            shufflePlayedIndexes.Clear();
        }
        streamingThrottle.Set();
        return interruptedSomething;
    }

    public async Task<bool> ProcessRequestAsync(SocketMessage message, IReadOnlyList<string> commandArgs)
    {
        if (commandArgs.Count >= 2 &&
            RequestIdentifiers.Contains(commandArgs[0], StringComparer.OrdinalIgnoreCase) &&
            commandArgs[1] is { } command &&
            !string.IsNullOrWhiteSpace(command))
        {
            var voiceChannel = ((IGuildUser)message.Author).VoiceChannel;
            void requireImInVoice()
            {
                if (connectedVoiceChannel is null)
                    throw new Exception("I am not currently in a voice channel");
            }
            void requireImNotInVoiceOrInSameVoiceChannel()
            {
                if (connectedVoiceChannel is not null && (voiceChannel?.Id != connectedVoiceChannel.Id))
                    throw new Exception("Command author is not in my voice channel");
            }
            void requireSameVoiceChannel()
            {
                if (voiceChannel?.Id != connectedVoiceChannel?.Id)
                    throw new Exception("Command author is not in my voice channel");
            }
            void requireTheyreInVoice()
            {
                if (voiceChannel is null)
                    throw new Exception("Command author is not in a voice channel");
            }
            if (commandArgs.Count == 2)
            {
                if (command.Equals("back", StringComparison.OrdinalIgnoreCase) || command.Equals("prev", StringComparison.OrdinalIgnoreCase) || command.Equals("previous", StringComparison.OrdinalIgnoreCase))
                {
                    requireImInVoice();
                    requireSameVoiceChannel();
                    skipCommand.Enqueue(true);
                    await message.Channel.SendMessageAsync("Moving to previous track.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                    return true;
                }
                if (command.Equals("join", StringComparison.OrdinalIgnoreCase))
                {
                    requireTheyreInVoice();
                    if (audioClient is not null)
                        await DisconnectAsync().ConfigureAwait(false);
                    await ConnectAsync(voiceChannel).ConfigureAwait(false);
                    await message.Channel.SendMessageAsync("I have joined your channel.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                    return true;
                }
                if (command.Equals("leave", StringComparison.OrdinalIgnoreCase))
                {
                    requireImInVoice();
                    requireSameVoiceChannel();
                    if (audioClient is not null)
                    {
                        await DisconnectAsync().ConfigureAwait(false);
                        await message.Channel.SendMessageAsync("I have left the channel I was in.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                    }
                    else
                        await message.Channel.SendMessageAsync("I am not currently in a channel.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                    return true;
                }
                if (command.Equals("normalize", StringComparison.OrdinalIgnoreCase))
                {
                    isLoudnessNormalized = !isLoudnessNormalized;
                    seekCommand.Enqueue(TimeSpan.FromSeconds(-1));
                    await message.Channel.SendMessageAsync($"Loudness normalization is now {(isLoudnessNormalized ? "on" : "off")}.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                    return true;
                }
                if (command.Equals("pause", StringComparison.OrdinalIgnoreCase))
                {
                    requireImInVoice();
                    requireSameVoiceChannel();
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
                    requireImInVoice();
                    requireSameVoiceChannel();
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
                if (command.Equals("skip", StringComparison.OrdinalIgnoreCase) || command.Equals("next", StringComparison.OrdinalIgnoreCase))
                {
                    requireImInVoice();
                    requireSameVoiceChannel();
                    skipCommand.Enqueue(true);
                    await message.Channel.SendMessageAsync("Moving to next track.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                    return true;
                }
                if (command.Equals("stop", StringComparison.OrdinalIgnoreCase))
                {
                    requireImInVoice();
                    requireSameVoiceChannel();
                    await StopAsync().ConfigureAwait(false);
                    await message.Channel.SendMessageAsync("Playback stopped.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                    return true;
                }
            }
            if (commandArgs.Count >= 2)
            {
                if (command.Equals("decibeladjust", StringComparison.OrdinalIgnoreCase))
                {
                    if (commandArgs.Count == 3 && double.TryParse(commandArgs[2], out var newDecibelAdjust))
                    {
                        decibelAdjust = newDecibelAdjust;
                        seekCommand.Enqueue(TimeSpan.FromSeconds(-1));
                    }
                    await message.Channel.SendMessageAsync($"Decibel adjustment is now {decibelAdjust}.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                    return true;
                }
            }
            if (commandArgs.Count == 3 && command.Equals("seek", StringComparison.OrdinalIgnoreCase))
            {
                requireImInVoice();
                requireSameVoiceChannel();
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
            if (commandArgs.Count >= 3)
            {
                if (command.Equals("play", StringComparison.OrdinalIgnoreCase))
                {
                    requireTheyreInVoice();
                    requireImNotInVoiceOrInSameVoiceChannel();
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
                                    await AddPlaylistItemAsync(fileInfo, voiceChannel).ConfigureAwait(false);
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
                                    var pleaseWaitMessage = await message.Channel.SendMessageAsync("Please wait as your YouTube playlist is downloaded...", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                                    await AddYouTubePlaylistPlaylistItemsAsync(playlistIdMatch.Value, voiceChannel).ConfigureAwait(false);
                                    await pleaseWaitMessage.ModifyAsync(msg => msg.Content = "The videos in the YouTube playlist were added to my playlist.").ConfigureAwait(false);
                                    return true;
                                }
                                var videoIdMatch = Regex.Match(string.Join(" ", commandArgs.Skip(3)), @"[0-9a-zA-Z_]{11}");
                                if (videoIdMatch.Success)
                                {
                                    var pleaseWaitMessage = await message.Channel.SendMessageAsync("Please wait as your YouTube video is downloaded...", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                                    await AddYouTubeVideoPlaylistItemAsync(videoIdMatch.Value, voiceChannel).ConfigureAwait(false);
                                    await pleaseWaitMessage.ModifyAsync(msg => msg.Content = "The YouTube video was added to the playlist.").ConfigureAwait(false);
                                    return true;
                                }
                                await foreach (var result in new YoutubeClient().Search.GetResultsAsync(string.Join(" ", commandArgs.Skip(3))))
                                    if (result is VideoSearchResult video)
                                    {
                                        var pleaseWaitMessage = await message.Channel.SendMessageAsync($"Please wait as this YouTube video is downloaded: https://youtu.be/{video.Id.Value}", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                                        await AddYouTubeVideoPlaylistItemAsync(video.Id.Value, voiceChannel).ConfigureAwait(false);
                                        await pleaseWaitMessage.ModifyAsync(msg => msg.Content = $"This YouTube video was added to the playlist: https://youtu.be/{video.Id.Value}").ConfigureAwait(false);
                                        return true;
                                    }
                                throw new Exception("YouTube media not found");
                            }
                        }
                    }
                    {
                        var youTubePlaylistId = Regex.Match(string.Join(" ", commandArgs.Skip(2)), @"[0-9a-zA-Z_]{34}");
                        if (youTubePlaylistId.Success)
                        {
                            var pleaseWaitMessage = await message.Channel.SendMessageAsync("Please wait as your YouTube playlist is downloaded...", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                            await AddYouTubePlaylistPlaylistItemsAsync(youTubePlaylistId.Value, voiceChannel).ConfigureAwait(false);
                            await pleaseWaitMessage.ModifyAsync(msg => msg.Content = "The videos in the YouTube playlist were added to my playlist.").ConfigureAwait(false);
                            return true;
                        }
                        var youtubeVideoIdMatch = Regex.Match(string.Join(" ", commandArgs.Skip(2)), @"[0-9a-zA-Z_]{11}");
                        if (youtubeVideoIdMatch.Success)
                        {
                            var pleaseWaitMessage = await message.Channel.SendMessageAsync("Please wait as your YouTube video is downloaded...", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                            await AddYouTubeVideoPlaylistItemAsync(youtubeVideoIdMatch.Value, voiceChannel).ConfigureAwait(false);
                            await pleaseWaitMessage.ModifyAsync(msg => msg.Content = "The YouTube video was added to the playlist.").ConfigureAwait(false);
                            return true;
                        }
                        if (commandArgs[2] is { } path &&
                            !string.IsNullOrWhiteSpace(path) &&
                            new FileInfo(path) is { } fileInfo &&
                            fileInfo.Exists)
                        {
                            await AddPlaylistItemAsync(fileInfo, voiceChannel).ConfigureAwait(false);
                            await message.Channel.SendMessageAsync("The local file was added to the playlist.", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                            return true;
                        }
                        await foreach (var result in new YoutubeClient().Search.GetResultsAsync(string.Join(" ", commandArgs.Skip(2))))
                            if (result is VideoSearchResult video)
                            {
                                await AddYouTubeVideoPlaylistItemAsync(video.Id.Value, voiceChannel).ConfigureAwait(false);
                                await message.Channel.SendMessageAsync($"This YouTube video was added to the playlist: https://youtu.be/{video.Id.Value}", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                                return true;
                            }
                        throw new Exception("Media not found");
                    }
                }
                if (command.Equals("say", StringComparison.OrdinalIgnoreCase) || command.Equals(value: "speak", StringComparison.OrdinalIgnoreCase))
                {
                    requireTheyreInVoice();
                    requireImNotInVoiceOrInSameVoiceChannel();
                    var pleaseWaitMessage = await message.Channel.SendMessageAsync("Please wait as your speech is synthesized...", messageReference: new MessageReference(message.Id)).ConfigureAwait(false);
                    await AddPlaylistItemAsync(SynthesizeSpeech(string.Join(" ", commandArgs.Skip(2))), voiceChannel).ConfigureAwait(false);
                    await pleaseWaitMessage.ModifyAsync(msg => msg.Content = "Your statement was added to the playlist.").ConfigureAwait(false);
                    return true;
                }
            }
        }
        return false;
    }

    const int bufferSize = 4096;

    static Process CreateFfmpegInstance(string path, bool isLoudnessNormalized, double decibelAdjust, TimeSpan? seekTo = null)
    {
        var arguments = $"-hide_banner -loglevel panic {(seekTo is { } nonNullSeekTo ? $"-ss {nonNullSeekTo.Days * 24 + nonNullSeekTo.Hours:00}:{nonNullSeekTo.Minutes:00}:{nonNullSeekTo.Seconds:00} " : "")}-i \"{path}\" {(isLoudnessNormalized ? "-filter:a loudnorm " : string.Empty)}{(decibelAdjust != 0D ? $"-filter:a \"volume={decibelAdjust}dB\" " : string.Empty)}-ac 2 -f s16le -ar 48000 pipe:1";
        if (Debugger.IsAttached)
            Console.WriteLine($"Executing: ffmpeg {arguments}");
        return Process.Start(new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
        });
    }

    static FileInfo SynthesizeSpeech(string text)
    {
        using var synthesizer = new SpeechSynthesizer();
        if (Settings.Instance.AudioVoiceName is { } voiceName)
            synthesizer.SelectVoice(voiceName);
        var fileInfo = new FileInfo(Path.GetTempFileName());
        fileInfo.Delete();
        synthesizer.SetOutputToWaveFile(fileInfo.FullName);
        synthesizer.Speak(text);
        return fileInfo;
    }
}
