// Live-camera capture + client-side downscale for /receipts/new (doc 0018).
//
// Gemini bills receipt images by pixel dimensions (~258 tokens per 768x768 tile), not
// bytes, so we cap the LONG EDGE before upload. A full phone photo (~3000x4000, ~24
// tiles, ~6.2k tokens) drops to ~6 tiles / ~1.5k tokens at a 1600px long edge with no
// meaningful loss on a single-column receipt. Both the live camera and the native file
// input funnel through the same canvas pipeline. Tune these two constants if a thermal
// receipt's digits ever blur (raise MAX_EDGE) or uploads feel heavy (lower it).
const MAX_EDGE = 1600;
const QUALITY = 0.85;

let stream = null;

// Attach the rear camera to the <video> viewfinder. Returns false (rather than throwing)
// when there's no secure context / permission, so the page can show a graceful hint.
export async function start(video) {
    try {
        stream = await navigator.mediaDevices.getUserMedia({
            video: { facingMode: { ideal: 'environment' } },
            audio: false,
        });
        video.srcObject = stream;
        await video.play();
        return true;
    } catch {
        stop();
        return false;
    }
}

export function stop() {
    if (stream) {
        for (const track of stream.getTracks()) {
            track.stop();
        }
        stream = null;
    }
}

// Grab the current viewfinder frame, downscale, hand the JPEG bytes back to .NET.
export async function capture(video) {
    if (!video.videoWidth || !video.videoHeight) {
        throw new Error('camera not ready yet');
    }
    const blob = await drawToJpeg(video, video.videoWidth, video.videoHeight);
    return toStreamRef(blob);
}

// Same downscale pipeline for a file picked via the native <input type="file">. The bytes
// arrive from .NET (Blazor InputFile) as a Uint8Array; wrap them back into a Blob to decode.
export async function resizeFile(bytes, type) {
    const bitmap = await createImageBitmap(new Blob([bytes], { type }));
    try {
        const blob = await drawToJpeg(bitmap, bitmap.width, bitmap.height);
        return toStreamRef(blob);
    } finally {
        bitmap.close();
    }
}

// --- internals -----------------------------------------------------------------------

async function drawToJpeg(source, width, height) {
    const scale = Math.min(1, MAX_EDGE / Math.max(width, height));
    const canvas = document.createElement('canvas');
    canvas.width = Math.round(width * scale);
    canvas.height = Math.round(height * scale);
    canvas.getContext('2d').drawImage(source, 0, 0, canvas.width, canvas.height);
    return await new Promise(resolve => canvas.toBlob(resolve, 'image/jpeg', QUALITY));
}

async function toStreamRef(blob) {
    if (!blob) {
        throw new Error('image encoding failed');
    }
    const buffer = await blob.arrayBuffer();
    return new Uint8Array(buffer);
}
