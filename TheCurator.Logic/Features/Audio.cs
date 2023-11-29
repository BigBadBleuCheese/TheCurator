using System.Runtime.Versioning;
using YoutubeExplode.Exceptions;
using YoutubeExplode.Playlists;

namespace TheCurator.Logic.Features;

public partial class Audio :
    SyncDisposable,
    IFeature
{
    public Audio(IBot bot)
    {
        this.bot = bot;
        connectedVoiceChannelUse = new(true);
        decibelAdjust = -12;
        isLoudnessNormalized = true;
        playedIndexes = new();
        playlist = new();
        playerAccess = new();
        seekCommand = new();
        skipCommand = new();
        shufflePlayedIndexes = new();
        streamingThrottle = new(true);
    }

    IApplicationCommand? audio;
    IAudioClient? audioClient;
    readonly IBot bot;
    IVoiceChannel? connectedVoiceChannel;
    readonly AsyncManualResetEvent connectedVoiceChannelUse;
    double decibelAdjust;
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

    public string Name =>
        "Audio";

    async Task AddPlaylistItemAsync(FileInfo fileInfo, IVoiceChannel voiceChannel)
    {
        using (await playerAccess.LockAsync())
            playlist.Add(fileInfo);
        await PlayAsync(voiceChannel);
    }

    void AddYouTubePlaylistPlaylistItems(string youtubeId, IVoiceChannel voiceChannel, IMessage? commandMessage = null, SocketSlashCommand? slashCommand = null)
    {
        _ = Task.Run(async () =>
        {
            var unavailableVideos = new List<PlaylistVideo>();
            var youtube = new YoutubeClient();
            await foreach (var video in youtube.Playlists.GetVideosAsync(youtubeId))
            {
                try
                {
                    await AddYouTubeVideoPlaylistItemAsync(video.Id.Value, voiceChannel);
                }
                catch (VideoUnavailableException)
                {
                    unavailableVideos.Add(video);
                }
            }
            if (unavailableVideos.Count > 0)
            {
                if (commandMessage is not null)
                    await commandMessage.Channel.SendMessageAsync($"The following videos from the playlist were not available:\r\n{string.Join("\r\n", unavailableVideos.Select(unavailableVideo => $"• \"{unavailableVideo.Title}\" from channel {unavailableVideo.Author.ChannelTitle} (https://youtu.be/{unavailableVideo.Id.Value})"))}", messageReference: new MessageReference(commandMessage.Id));
                else if (slashCommand is not null)
                    await slashCommand.User.SendMessageAsync($"The following videos from the playlist were not available:\r\n{string.Join("\r\n", unavailableVideos.Select(unavailableVideo => $"• \"{unavailableVideo.Title}\" from channel {unavailableVideo.Author.ChannelTitle} (https://youtu.be/{unavailableVideo.Id.Value})"))}");
            }
        });
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
            await youtube.Videos.Streams.DownloadAsync((await youtube.Videos.Streams.GetManifestAsync(youtubeId)).GetAudioOnlyStreams().GetWithHighestBitrate(), cacheFileInfo.FullName);
        }
        await AddPlaylistItemAsync(cacheFileInfo, voiceChannel);
    }

    async Task ConnectAsync(IVoiceChannel voiceChannel)
    {
        if (audioClient is not null)
            throw new Exception("Already connected");
        audioClient = await voiceChannel.ConnectAsync(true);
        connectedVoiceChannel = voiceChannel;
    }

    public async Task CreateGlobalApplicationCommandsAsync()
    {
        audio = await bot.Client.CreateGlobalApplicationCommandAsync(new SlashCommandBuilder()
            .WithName("audio")
            .WithDescription("Allows The Curator to interact with voice channels")
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("back")
                .WithDescription("Move to the previous track")
                .WithType(ApplicationCommandOptionType.SubCommand))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("decibeladjust")
                .WithDescription("Specify the positive or negative amount of decibels by which to adjust the audio volume")
                .AddOption("delta", ApplicationCommandOptionType.Number, "The amount of decibels by which to adjust the audio volume", true)
                .WithType(ApplicationCommandOptionType.SubCommand))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("join")
                .WithDescription("The Curator joins your current voice channel")
                .WithType(ApplicationCommandOptionType.SubCommand))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("leave")
                .WithDescription("The Curator leaves whatever voice channel in which it currently is")
                .WithType(ApplicationCommandOptionType.SubCommand))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("normalization")
                .WithDescription("Toggle loudness normalization")
                .AddOption("enabled", ApplicationCommandOptionType.Boolean, "Whether audio normalization is enabled", true)
                .WithType(ApplicationCommandOptionType.SubCommand))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("pause")
                .WithDescription("Pauses playback or resumes it if it is already paused")
                .WithType(ApplicationCommandOptionType.SubCommand))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("play")
                .WithDescription("Adds something to the playlist from a service")
                .AddOption("content", ApplicationCommandOptionType.String, "The content to add to the playlist", true)
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("service")
                    .WithDescription("The service from which to add content to the playlist")
                    .AddChoice("Local", "local")
                    .AddChoice("YouTube", "youtube")
                    .WithType(ApplicationCommandOptionType.String))
                .WithType(ApplicationCommandOptionType.SubCommand))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("repeat")
                .WithDescription("Change the repeat mode")
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("mode")
                    .WithDescription("The repeat mode")
                    .AddChoice("None", 0)
                    .AddChoice("Playlist", 1)
                    .AddChoice("Single", 2)
                    .WithType(ApplicationCommandOptionType.Integer)
                    .WithRequired(true))
                .WithType(ApplicationCommandOptionType.SubCommand))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("resume")
                .WithDescription("Resumes playback if it is paused")
                .WithType(ApplicationCommandOptionType.SubCommand))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("say")
                .WithDescription("Convert text to speech and play it")
                .AddOption("text", ApplicationCommandOptionType.String, "The text to speak", true)
                .WithType(ApplicationCommandOptionType.SubCommand))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("seek")
                .WithDescription("Seek to a position in time in the current track")
                .AddOption("position", ApplicationCommandOptionType.String, "The position to which to seek in seconds or time format", true)
                .WithType(ApplicationCommandOptionType.SubCommand))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("shuffle")
                .WithDescription("Toggle shuffling")
                .AddOption("enabled", ApplicationCommandOptionType.Boolean, "Whether shuffling is enabled", true)
                .WithType(ApplicationCommandOptionType.SubCommand))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("skip")
                .WithDescription("Move to the next track")
                .WithType(ApplicationCommandOptionType.SubCommand))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("stop")
                .WithDescription("Stops playing any audio that it is currently playing")
                .WithType(ApplicationCommandOptionType.SubCommand))
            .Build());
    }

    async Task DisconnectAsync()
    {
        await StopAsync();
        if (audioClient is not null)
        {
            await Task.WhenAll(connectedVoiceChannelUse.WaitAsync(), Task.Delay(TimeSpan.FromSeconds(0.25)));
            if (audioClient is not null)
            {
                await audioClient.StopAsync();
                audioClient.Dispose();
                audioClient = null;
            }
            connectedVoiceChannel = null;
        }
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
            await ConnectAsync(voiceChannel);
            announceEntrance = true;
        }
        using (await playerAccess.LockAsync())
        {
            if (playerCancellationTokenSource is not null)
                return;
            playerCancellationTokenSource = new();
            _ = Task.Run(() => PlayerLogicAsync(announceEntrance));
        }
    }

    async Task PlayAudioAsync(AudioOutStream discordInputStream, FileInfo fileInfo, bool isSkippable, bool isPsa = false)
    {
        var cancellationToken = playerCancellationTokenSource?.Token ?? CancellationToken.None;
        var playTime = new Stopwatch();
        while (true)
        {
            using var ffmpegInstance = seekCommand.TryDequeue(out var seekTo) ? CreateFfmpegInstance(fileInfo.FullName, isPsa, isLoudnessNormalized, decibelAdjust, seekTo < TimeSpan.Zero ? playTime.Elapsed : seekTo) : CreateFfmpegInstance(fileInfo.FullName, isPsa, isLoudnessNormalized, decibelAdjust);
            using var ffmpegOutputStream = ffmpegInstance.StandardOutput.BaseStream;
            if (!playTime.IsRunning)
                playTime.Start();
            var bytesRead = -1;
            while (bytesRead != 0 && ffmpegOutputStream.CanRead)
            {
                await streamingThrottle.WaitAsync();
                if (seekCommand.TryPeek(out _) || (isSkippable && skipCommand.TryPeek(out _)))
                    break;
                var buffer = new byte[bufferSize];
                bytesRead = await ffmpegOutputStream.ReadAsync(buffer, 0, bufferSize);
                using var bufferStream = new MemoryStream(buffer);
                await bufferStream.CopyToAsync(discordInputStream, cancellationToken);
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
        connectedVoiceChannelUse.Reset();
        using var discordInputStream = audioClient.CreatePCMStream(AudioApplication.Mixed);
        var appDirectoryInfo = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        var resourcesDirectoryInfo = new DirectoryInfo(Path.Combine(appDirectoryInfo.FullName, "Resources"));
        if (announceEntrance && resourcesDirectoryInfo.Exists)
        {
            var entranceSoundFileInfo = new FileInfo(Path.Combine(resourcesDirectoryInfo.FullName, "546638.mp3"));
            if (entranceSoundFileInfo.Exists)
                await PlayAudioAsync(discordInputStream, entranceSoundFileInfo, false);
        }
        try
        {
            try
            {
                while (playerCancellationTokenSource is not null)
                {
                    seekCommand.Clear();
                    FileInfo? fileInfo = null;
                    var playedIndexesCount = 0;
                    using (await playerAccess.LockAsync())
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
                    if (fileInfo is not null)
                    {
                        var getPSAFileInfoTask = Task.Run(() =>
                        {
                            var settings = Settings.Instance;
                            if (OperatingSystem.IsWindows() &&
                                settings.AudioPSAs is { } psas &&
                                psas.Length > 0 &&
                                settings.AudioPSAFrequency is { } psaFrequency &&
                                psaFrequency > 0 &&
                                playedIndexesCount % psaFrequency == 0)
                                return SynthesizeSpeech(psas[new Random().Next(0, psas.Length)]);
                            return null;
                        });
                        await PlayAudioAsync(discordInputStream, fileInfo, true);
                        if (await getPSAFileInfoTask is { } psaFileInfo)
                            await PlayAudioAsync(discordInputStream, psaFileInfo, false, true);
                    }
                    else
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                // alrighty then
            }
            finally
            {
                if (resourcesDirectoryInfo.Exists)
                {
                    var exitSoundFile = new FileInfo(Path.Combine(resourcesDirectoryInfo.FullName, "546641.mp3"));
                    if (exitSoundFile.Exists)
                        await PlayAudioAsync(discordInputStream, exitSoundFile, false);
                }
            }
        }
        finally
        {
            await discordInputStream.FlushAsync();
            connectedVoiceChannelUse.Set();
        }
        await DisconnectAsync();
    }

    async Task StopAsync()
    {
        using (await playerAccess.LockAsync())
        {
            if (playerCancellationTokenSource is { } cancellationTokenSource)
            {
                playerCancellationTokenSource = null;
                cancellationTokenSource.Cancel();
                cancellationTokenSource.Dispose();
            }
            playlist.Clear();
            playedIndexes.Clear();
            shufflePlayedIndexes.Clear();
        }
        streamingThrottle.Set();
    }

    public async Task ProcessCommandAsync(SocketSlashCommand command)
    {
        if (command.CommandId == audio?.Id)
        {
            await command.DeferAsync();
            var voiceChannel = ((IGuildUser)command.User).VoiceChannel;
            async Task<bool> requireImInVoiceAsync()
            {
                if (connectedVoiceChannel is null)
                {
                    await command.RespondAsync("I am not currently in a voice channel.");
                    return false;
                }
                return true;
            }
            async Task<bool> requireImNotInVoiceOrInSameVoiceChannelAsync()
            {
                if (connectedVoiceChannel is not null && (voiceChannel?.Id != connectedVoiceChannel.Id))
                {
                    await command.RespondAsync("Command author is not in my voice channel.");
                    return false;
                }
                return true;
            }
            async Task<bool> requireSameVoiceChannelAsync()
            {
                if (voiceChannel?.Id != connectedVoiceChannel?.Id)
                {
                    await command.RespondAsync("Command author is not in my voice channel.");
                    return false;
                }
                return true;
            }
            async Task<bool> requireTheyreInVoiceAsync()
            {
                if (voiceChannel is null)
                {
                    await command.RespondAsync("Command author is not in a voice channel.");
                    return false;
                }
                return true;
            }
            var subCommand = command.Data.Options.First();
            switch (subCommand.Name)
            {
                case "back":
                    if (await requireImInVoiceAsync() && await requireSameVoiceChannelAsync())
                    {
                        skipCommand.Enqueue(true);
                        await command.FollowupAsync("Moving to previous track.");
                    }
                    break;
                case "decibeladjust":
                    if (subCommand.Options.First().Value is double setDecibelAdjust && setDecibelAdjust != decibelAdjust)
                    {
                        decibelAdjust = setDecibelAdjust;
                        seekCommand.Enqueue(TimeSpan.FromSeconds(-1));
                    }
                    await command.FollowupAsync($"Decibel adjustment is now {decibelAdjust}.");
                    break;
                case "join":
                    if (await requireTheyreInVoiceAsync())
                    {
                        if (audioClient is not null)
                            await DisconnectAsync();
                        await ConnectAsync(voiceChannel);
                        await command.FollowupAsync("I have joined your channel.");
                    }
                    break;
                case "leave":
                    if (await requireImInVoiceAsync() && await requireSameVoiceChannelAsync())
                    {
                        if (audioClient is not null)
                        {
                            await DisconnectAsync();
                            await command.FollowupAsync("I have left the channel I was in.");
                        }
                        else
                            await command.FollowupAsync("I am not currently in a channel.");
                    }
                    break;
                case "normalization":
                    if (subCommand.Options.First().Value is bool setLoudnessNormalized && setLoudnessNormalized != isLoudnessNormalized)
                    {
                        isLoudnessNormalized = setLoudnessNormalized;
                        seekCommand.Enqueue(TimeSpan.FromSeconds(-1));
                    }
                    await command.FollowupAsync($"Loudness normalization is now {(isLoudnessNormalized ? "on" : "off")}.");
                    break;
                case "pause":
                    if (await requireImInVoiceAsync() && await requireSameVoiceChannelAsync())
                    {
                        if (streamingThrottle.IsSet)
                        {
                            streamingThrottle.Reset();
                            await command.FollowupAsync("Playback paused.");
                        }
                        else
                        {
                            streamingThrottle.Set();
                            await command.FollowupAsync("Playback resumed.");
                        }
                    }
                    break;
                case "play":
                    if (await requireTheyreInVoiceAsync() && await requireImNotInVoiceOrInSameVoiceChannelAsync())
                    {
                        var content = (string)subCommand.Options.First(option => option.Name == "content")!.Value;
                        var service = subCommand.Options.FirstOrDefault(option => option.Name == "service")?.Value?.ToString();
                        if (!string.IsNullOrWhiteSpace(service))
                        {
                            if (service == "local" && new FileInfo(content) is { } fileInfo)
                            {
                                if (fileInfo.Exists)
                                {
                                    await AddPlaylistItemAsync(fileInfo, voiceChannel);
                                    await command.FollowupAsync($"Added {fileInfo.Name} to the playlist.");
                                }
                                else
                                    await command.FollowupAsync($"The specified file does not exist.");
                            }
                            if (service == "youtube")
                            {
                                var playlistIdMatch = GetYouTubePlaylistIdPattern().Match(content);
                                if (playlistIdMatch.Success)
                                {
                                    AddYouTubePlaylistPlaylistItems(playlistIdMatch.Groups["playlistId"].Value, voiceChannel, slashCommand: command);
                                    await command.FollowupAsync("The videos in the YouTube playlist were added to my playlist.");
                                    return;
                                }
                                var videoIdMatch = GetYouTubeVideoIdPattern().Match(content);
                                if (videoIdMatch.Success)
                                {
                                    await AddYouTubeVideoPlaylistItemAsync(videoIdMatch.Value, voiceChannel);
                                    await command.FollowupAsync("The YouTube video was added to the playlist.");
                                    return;
                                }
                                await foreach (var result in new YoutubeClient().Search.GetResultsAsync(content))
                                    if (result is VideoSearchResult video)
                                    {
                                        await AddYouTubeVideoPlaylistItemAsync(video.Id.Value, voiceChannel);
                                        await command.FollowupAsync($"This YouTube video was added to the playlist: https://youtu.be/{video.Id.Value}");
                                        return;
                                    }
                                await command.FollowupAsync("YouTube media not found.");
                            }
                        }
                        {
                            var youTubePlaylistId = GetYouTubePlaylistIdPattern().Match(content);
                            if (youTubePlaylistId.Success)
                            {
                                AddYouTubePlaylistPlaylistItems(youTubePlaylistId.Groups["playlistId"].Value, voiceChannel, slashCommand: command);
                                await command.FollowupAsync("The videos in the YouTube playlist were added to my playlist.");
                                return;
                            }
                            var youtubeVideoIdMatch = GetYouTubeVideoIdPattern().Match(content);
                            if (youtubeVideoIdMatch.Success)
                            {
                                await AddYouTubeVideoPlaylistItemAsync(youtubeVideoIdMatch.Value, voiceChannel);
                                await command.FollowupAsync("The YouTube video was added to the playlist.");
                                return;
                            }
                            if (!string.IsNullOrWhiteSpace(content) &&
                                new FileInfo(content) is { } fileInfo &&
                                fileInfo.Exists)
                            {
                                await AddPlaylistItemAsync(fileInfo, voiceChannel);
                                await command.FollowupAsync("The local file was added to the playlist.");
                                return;
                            }
                            await foreach (var result in new YoutubeClient().Search.GetResultsAsync(content))
                                if (result is VideoSearchResult video)
                                {
                                    await AddYouTubeVideoPlaylistItemAsync(video.Id.Value, voiceChannel);
                                    await command.FollowupAsync($"This YouTube video was added to the playlist: https://youtu.be/{video.Id.Value}");
                                    return;
                                }
                            await command.FollowupAsync("Media not found.");
                        }
                    }
                    break;
                case "repeat":
                    if (subCommand.Options.First().Value is long setRepeatMode && (RepeatMode)setRepeatMode != repeatMode)
                        repeatMode = (RepeatMode)setRepeatMode;
                    switch (repeatMode)
                    {
                        case RepeatMode.Playlist:
                            await command.FollowupAsync("Repeating the playlist.");
                            break;
                        case RepeatMode.Single:
                            await command.FollowupAsync("Repeating the current track.");
                            break;
                        default:
                            await command.FollowupAsync("Repeating disabled.");
                            break;
                    }
                    break;
                case "resume":
                    if (await requireImInVoiceAsync() && await requireSameVoiceChannelAsync())
                    {
                        streamingThrottle.Set();
                        await command.FollowupAsync("Playback resumed.");
                    }
                    break;
                case "say":
                    if (OperatingSystem.IsWindows())
                    {
                        if (await requireTheyreInVoiceAsync() && await requireImNotInVoiceOrInSameVoiceChannelAsync() && subCommand.Options.First().Value is string text)
                        {
                            await AddPlaylistItemAsync(SynthesizeSpeech(text), voiceChannel);
                            await command.FollowupAsync("Your statement was added to the playlist.");
                            return;
                        }
                    }
                    else
                        await command.FollowupAsync("Text-to-speech is not supported on this operating system.");
                    break;
                case "seek":
                    if (await requireImInVoiceAsync() && await requireSameVoiceChannelAsync())
                    {
                        var isPlaying = false;
                        using (await playerAccess.LockAsync())
                            isPlaying = playerCancellationTokenSource is not null;
                        if (isPlaying)
                        {
                            if (subCommand.Options.First().Value is string positionText)
                            {
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
                                    await command.FollowupAsync($"Seeking to: {nonNullPosition}.");
                                }
                                else
                                    await command.FollowupAsync("Cannot comprehend specified position.");
                            }
                        }
                        else
                            await command.FollowupAsync("Nothing is playing.");
                    }
                    break;
                case "shuffle":
                    if (subCommand.Options.First().Value is bool setShuffling && setShuffling != isShuffling)
                        isShuffling = setShuffling;
                    await command.FollowupAsync(isShuffling ? "Shuffling." : "Not shuffling.");
                    break;
                case "skip":
                    if (await requireImInVoiceAsync() && await requireSameVoiceChannelAsync())
                    {
                        skipCommand.Enqueue(true);
                        await command.FollowupAsync("Moving to next track.");
                    }
                    break;
                case "stop":
                    if (await requireImInVoiceAsync() && await requireSameVoiceChannelAsync())
                    {
                        await StopAsync();
                        await command.FollowupAsync("Playback stopped.");
                    }
                    break;
            }
        }
    }

    const int bufferSize = 4096;

    static Process CreateFfmpegInstance(string path, bool isPsa, bool isLoudnessNormalized, double decibelAdjust, TimeSpan? seekTo = null)
    {
        var arguments = $"-hide_banner -loglevel panic {(seekTo is { } nonNullSeekTo ? $"-ss {nonNullSeekTo.Days * 24 + nonNullSeekTo.Hours:00}:{nonNullSeekTo.Minutes:00}:{nonNullSeekTo.Seconds:00} " : "")}-i \"{path}\" {(isLoudnessNormalized ? "-filter:a loudnorm " : string.Empty)}{(!isPsa && decibelAdjust != 0D ? $"-filter:a \"volume={decibelAdjust}dB\" " : string.Empty)}-ac 2 -f s16le -ar 48000 pipe:1";
        if (Debugger.IsAttached)
            Console.WriteLine($"Executing: ffmpeg {arguments}");
        return Process.Start(new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
        })!;
    }

    [GeneratedRegex(@"[0-9a-zA-Z_\-]{11}")]
    private static partial Regex GetYouTubeVideoIdPattern();

    [GeneratedRegex(@"\?list\=(?<playlistId>[0-9a-zA-Z_\-]+)")]
    private static partial Regex GetYouTubePlaylistIdPattern();

    [SupportedOSPlatform("windows")]
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
