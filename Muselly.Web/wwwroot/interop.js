// Browser interop for the Muselly web client: the page origin (for the API base URL) and a single shared
// HTMLAudioElement that streams Opus from the host's /api/stream endpoint. The managed BrowserAudio service
// polls position/duration/ended through these functions.

let audio = null;
let ended = false;

export function getOrigin() {
    return globalThis.location.origin;
}

export function getPath() {
    return globalThis.location.pathname + globalThis.location.search;
}

export function setPath(path) {
    try { globalThis.history.replaceState(null, "", path); } catch (e) { /* ignore */ }
}

export function audioInit() {
    if (audio) return;
    audio = new Audio();
    audio.preload = "auto";
    audio.addEventListener("ended", () => { ended = true; });
    audio.addEventListener("playing", () => { ended = false; });
}

export function audioPlay(url) {
    audioInit();
    ended = false;
    audio.src = url;
    const p = audio.play();
    if (p && p.catch) p.catch(() => {});
}

export function audioPause() {
    if (audio) audio.pause();
}

export function audioResume() {
    if (audio) {
        const p = audio.play();
        if (p && p.catch) p.catch(() => {});
    }
}

export function audioStop() {
    if (audio) {
        audio.pause();
        audio.removeAttribute("src");
        audio.load();
    }
    ended = false;
}

export function audioSeek(seconds) {
    if (audio) {
        try { audio.currentTime = seconds; } catch (e) { /* not seekable yet */ }
    }
}

export function audioSetVolume(v) {
    if (audio) audio.volume = v;
}

export function audioGetTime() {
    return audio ? (audio.currentTime || 0) : 0;
}

export function audioGetDuration() {
    return (audio && isFinite(audio.duration)) ? audio.duration : 0;
}

export function audioEnded() {
    return ended;
}
