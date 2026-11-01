#import <Foundation/Foundation.h>
#import <MediaPlayer/MediaPlayer.h>

typedef void (*YmpdMediaCommandCallback)(int command);

static YmpdMediaCommandCallback callback;
static MPRemoteCommand *commands[5];
static id tokens[5];
static BOOL claimedNowPlaying;

static void registerCommand(MPRemoteCommand *command, int index) {
    commands[index] = command;
    command.enabled = YES;
    tokens[index] = [command addTargetWithHandler:^MPRemoteCommandHandlerStatus(MPRemoteCommandEvent *event) {
        if (callback != NULL) callback(index);
        return MPRemoteCommandHandlerStatusSuccess;
    }];
}

void ympd_media_start(YmpdMediaCommandCallback handler) {
    callback = handler;
    MPRemoteCommandCenter *center = [MPRemoteCommandCenter sharedCommandCenter];
    registerCommand(center.previousTrackCommand, 0);
    registerCommand(center.togglePlayPauseCommand, 1);
    registerCommand(center.nextTrackCommand, 2);
    registerCommand(center.playCommand, 3);
    registerCommand(center.pauseCommand, 4);
}

void ympd_media_update(const char *title, const char *artist, const char *album,
                       const char *composer, double duration, double elapsed, int state) {
    MPNowPlayingInfoCenter *center = [MPNowPlayingInfoCenter defaultCenter];
    if (title == NULL || title[0] == '\0') title = "modern ympd";

    NSMutableDictionary *info = [NSMutableDictionary dictionary];
    info[MPMediaItemPropertyTitle] = [NSString stringWithUTF8String:title];
    if (artist != NULL && artist[0] != '\0')
        info[MPMediaItemPropertyArtist] = [NSString stringWithUTF8String:artist];
    if (album != NULL && album[0] != '\0')
        info[MPMediaItemPropertyAlbumTitle] = [NSString stringWithUTF8String:album];
    if (composer != NULL && composer[0] != '\0')
        info[MPMediaItemPropertyComposer] = [NSString stringWithUTF8String:composer];
    if (duration > 0)
        info[MPMediaItemPropertyPlaybackDuration] = @(duration);
    info[MPNowPlayingInfoPropertyMediaType] = @(MPNowPlayingInfoMediaTypeAudio);
    info[MPNowPlayingInfoPropertyDefaultPlaybackRate] = @(1.0);
    info[MPNowPlayingInfoPropertyElapsedPlaybackTime] = @(MAX(0, elapsed));
    if (!claimedNowPlaying) {
        // macOS routes media keys only after the app has become Now Playing.
        // Claim that role once, then immediately publish MPD's actual state.
        info[MPNowPlayingInfoPropertyPlaybackRate] = @(1.0);
        center.nowPlayingInfo = info;
        center.playbackState = MPNowPlayingPlaybackStatePlaying;
        claimedNowPlaying = YES;
    }
    info[MPNowPlayingInfoPropertyPlaybackRate] = @(state == 1 ? 1.0 : 0.0);
    center.nowPlayingInfo = info;
    center.playbackState = state == 1 ? MPNowPlayingPlaybackStatePlaying
                         : MPNowPlayingPlaybackStatePaused;
}

void ympd_media_stop(void) {
    callback = NULL;
    for (int index = 0; index < 5; index++) {
        if (tokens[index] != nil) {
            [commands[index] removeTarget:tokens[index]];
            tokens[index] = nil;
            commands[index] = nil;
        }
    }
    MPNowPlayingInfoCenter *center = [MPNowPlayingInfoCenter defaultCenter];
    center.nowPlayingInfo = nil;
    center.playbackState = MPNowPlayingPlaybackStateStopped;
    claimedNowPlaying = NO;
}
