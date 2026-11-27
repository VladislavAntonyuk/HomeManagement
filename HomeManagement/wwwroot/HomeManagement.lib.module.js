// Blazor JavaScript initializer, loaded automatically by blazor.web.js.

// Resuming a paused circuit fails in Blazor 11 when the connection was re-established before the pause:
// the server answers "The circuit host '...' has already been initialized" and the page stays broken behind the error bar.
// Phones drop the connection often, and auto-pause pauses the circuit after the page is hidden, so this happened when coming back to the app.
// A fresh page is the only way to continue then, the same as when the server no longer has the circuit's state.
export function afterServerStarted(blazor) {
    const resumeCircuit = blazor.resumeCircuit;
    blazor.resumeCircuit = async () => {
        try {
            if (await resumeCircuit()) {
                return true;
            }
        } catch {
            // Reloaded below.
        }

        location.reload();
        return false;
    };
}
