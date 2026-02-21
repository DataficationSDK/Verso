window.versoSaveDialog = {
    _fileHandle: null,

    /**
     * Show the native OS save file picker and write content to the chosen file.
     * Returns { status, fileName } where status is 'saved', 'cancelled', 'unsupported', or 'error'.
     */
    saveAs: async function (suggestedName, content) {
        if (!('showSaveFilePicker' in window)) {
            return { status: 'unsupported' };
        }
        try {
            var handle = await window.showSaveFilePicker({
                suggestedName: suggestedName,
                types: [{
                    description: 'Verso Notebook',
                    accept: { 'application/json': ['.verso'] }
                }]
            });
            var writable = await handle.createWritable();
            await writable.write(content);
            await writable.close();
            this._fileHandle = handle;
            return { status: 'saved', fileName: handle.name };
        } catch (e) {
            if (e.name === 'AbortError') {
                return { status: 'cancelled' };
            }
            return { status: 'error', message: e.message };
        }
    },

    /**
     * Write content to the previously saved file handle (no dialog).
     */
    save: async function (content) {
        if (!this._fileHandle) {
            return { status: 'no_handle' };
        }
        try {
            var writable = await this._fileHandle.createWritable();
            await writable.write(content);
            await writable.close();
            return { status: 'saved', fileName: this._fileHandle.name };
        } catch (e) {
            return { status: 'error', message: e.message };
        }
    },

    hasHandle: function () {
        return this._fileHandle !== null;
    },

    clearHandle: function () {
        this._fileHandle = null;
    }
};
