namespace Verso.JavaScript.Kernel;

/// <summary>
/// Contains the Node.js bridge script as a string constant. This script is written to a
/// temp file and executed as a persistent subprocess. It communicates with .NET via NDJSON
/// over stdin/stdout.
/// </summary>
internal static class NodeBridgeScript
{
    public const string BridgeSource = """
        'use strict';
        const vm = require('vm');
        const readline = require('readline');

        // --- Dynamic import() support for vm.Script ---
        // Node 20.8+ provides USE_MAIN_CONTEXT_DEFAULT_LOADER which delegates
        // import() inside vm scripts to the main context's ESM loader.
        // For Node 18-20.7, we fall back to a callback that calls the host import().
        const _verso_dynamicImport = (() => {
            try {
                const c = vm.constants;
                if (c && c.USE_MAIN_CONTEXT_DEFAULT_LOADER !== undefined) {
                    return c.USE_MAIN_CONTEXT_DEFAULT_LOADER;
                }
            } catch (_) {}
            return async function(specifier) {
                return import(specifier);
            };
        })();

        // --- Expose CJS globals to vm context ---
        globalThis.require = require;
        globalThis.module = module;
        globalThis.__filename = __filename;
        globalThis.__dirname = __dirname;

        // --- Snapshot of initial globals for tracking user-defined variables ---
        const _verso_initialGlobals = new Set(Object.getOwnPropertyNames(globalThis));
        _verso_initialGlobals.add('_verso_initialGlobals');

        // --- Output capture ---
        let _verso_stdoutBuf = [];
        let _verso_stderrBuf = [];

        console.log = (...args) => { _verso_stdoutBuf.push(args.map(String).join(' ')); };
        console.error = (...args) => { _verso_stderrBuf.push(args.map(String).join(' ')); };
        console.warn = (...args) => { _verso_stderrBuf.push(args.map(String).join(' ')); };
        console.info = (...args) => { _verso_stdoutBuf.push(args.map(String).join(' ')); };

        function _verso_flushOutput() {
            const stdout = _verso_stdoutBuf.join('\n');
            const stderr = _verso_stderrBuf.join('\n');
            _verso_stdoutBuf = [];
            _verso_stderrBuf = [];
            return { stdout, stderr };
        }

        // --- User globals tracking ---
        function _verso_getUserGlobals() {
            const userGlobals = [];
            for (const key of Object.getOwnPropertyNames(globalThis)) {
                if (_verso_initialGlobals.has(key)) continue;
                if (key.startsWith('_verso')) continue;
                const val = globalThis[key];
                if (typeof val === 'function' || typeof val === 'undefined') continue;
                userGlobals.push(key);
            }
            return userGlobals;
        }

        // --- Last-expression capture ---
        function _verso_tryEvalLastExpr(code) {
            const lines = code.split('\n');
            let lastLine = '';
            for (let i = lines.length - 1; i >= 0; i--) {
                const t = lines[i].trim();
                if (t && !t.startsWith('//') && !t.startsWith('*')) {
                    lastLine = t;
                    break;
                }
            }
            if (!lastLine) return null;

            const stmtPrefixes = [
                'const ', 'let ', 'var ', 'function ', 'class ',
                'if (', 'if(', 'for (', 'for(', 'while (', 'while(',
                'switch (', 'switch(', 'try ', 'try{',
                'return ', 'throw ', 'import ', 'export ',
            ];
            if (stmtPrefixes.some(p => lastLine.startsWith(p))) return null;
            if (/^[a-zA-Z_$][a-zA-Z0-9_$]*\s*=(?!=)/.test(lastLine)) return null;
            if (lastLine.endsWith('{') || lastLine.endsWith('}')) return null;

            try {
                const s = new vm.Script(`(${lastLine})`, { filename: '<lastexpr>', importModuleDynamically: _verso_dynamicImport });
                const result = s.runInThisContext();
                if (result === undefined || typeof result === 'function') return null;
                return JSON.stringify(result, null, 2) ?? null;
            } catch (_) {
                return null;
            }
        }

        // --- Declaration promotion ---
        // Extracts top-level const/let/var/class/function names so they can be
        // promoted to globalThis after execution inside a function scope. This
        // enables re-running cells that use const/let without "already declared" errors.
        function _verso_extractTopLevelDecls(code) {
            const declNames = [];
            const lines = code.split('\n');
            let braceDepth = 0;
            for (const line of lines) {
                const trimmed = line.trim();
                // Check BEFORE updating brace depth so opening-brace lines are top-level
                if (braceDepth === 0) {
                    const match = trimmed.match(/^(?:const|let|var|class|(?:async\s+)?function\*?)\s+([a-zA-Z_$][a-zA-Z0-9_$]*)/);
                    if (match) declNames.push(match[1]);
                }
                for (const ch of trimmed) {
                    if (ch === '{') braceDepth++;
                    else if (ch === '}') braceDepth--;
                }
            }
            return declNames;
        }

        function _verso_hasAwait(code) {
            // Check for await keyword not inside a string or comment
            // Simple heuristic: look for await as a standalone word
            return /\bawait\b/.test(code);
        }

        // --- Command handlers ---
        async function _verso_handleExecute(cmd) {
            _verso_stdoutBuf = [];
            _verso_stderrBuf = [];
            let lastExpr = null;
            let error = null;
            try {
                // Always wrap in a function scope so const/let/class declarations
                // are block-scoped and can be re-declared on cell re-execution.
                // Promote all top-level declarations to globalThis for cross-cell access.
                const declNames = _verso_extractTopLevelDecls(cmd.code);
                const promotion = declNames.map(n =>
                    `globalThis[${JSON.stringify(n)}] = typeof ${n} !== 'undefined' ? ${n} : undefined;`
                ).join('\n');

                if (_verso_hasAwait(cmd.code)) {
                    const wrapped = `(async () => {\n${cmd.code}\n${promotion}\n})()`;
                    const script = new vm.Script(wrapped, { filename: '<cell>', lineOffset: -1, importModuleDynamically: _verso_dynamicImport });
                    await script.runInThisContext();
                } else {
                    const wrapped = `(function() {\n${cmd.code}\n${promotion}\n})()`;
                    const script = new vm.Script(wrapped, { filename: '<cell>', lineOffset: -1, importModuleDynamically: _verso_dynamicImport });
                    script.runInThisContext();
                }
                lastExpr = _verso_tryEvalLastExpr(cmd.code);
            } catch (e) {
                error = { message: e.message ?? String(e), stack: e.stack ?? '' };
            }
            const { stdout, stderr } = _verso_flushOutput();
            const globals = _verso_getUserGlobals();
            return {
                type: 'executeResult', id: cmd.id,
                stdout, stderr, lastExpr, globals, error,
            };
        }

        function _verso_handleSetVariables(cmd) {
            for (const [name, jsonValue] of Object.entries(cmd.variables)) {
                try {
                    globalThis[name] = JSON.parse(jsonValue);
                } catch (_) {
                    globalThis[name] = jsonValue;
                }
            }
            return { type: 'setVariablesResult', id: cmd.id };
        }

        function _verso_handleGetVariables(cmd) {
            const result = {};
            for (const name of cmd.names) {
                const val = globalThis[name];
                try {
                    result[name] = val !== undefined ? JSON.stringify(val) : null;
                } catch (_) {
                    result[name] = val !== undefined ? String(val) : null;
                }
            }
            return { type: 'variablesResult', id: cmd.id, variables: result };
        }

        // --- Module path management ---
        const _verso_addedPaths = new Set();

        function _verso_handleAddModulePath(cmd) {
            const p = cmd.path;
            if (p && !_verso_addedPaths.has(p)) {
                _verso_addedPaths.add(p);
                // Add to the bridge module's own search paths so the exposed
                // require() can resolve packages installed via #!npm
                module.paths.unshift(p);
            }
            return { type: 'addModulePathResult', id: cmd.id };
        }

        // --- TypeScript transpilation ---
        let _verso_ts = null;
        let _verso_tsChecked = false;

        function _verso_ensureTypeScript() {
            if (_verso_tsChecked) return _verso_ts !== null;
            _verso_tsChecked = true;
            try {
                _verso_ts = require('typescript');
                return true;
            } catch (_) {
                return false;
            }
        }

        function _verso_transpileTypeScript(code) {
            if (!_verso_ensureTypeScript()) {
                return { error: 'TypeScript module not found. Install it with #!npm typescript' };
            }
            try {
                const result = _verso_ts.transpileModule(code, {
                    compilerOptions: {
                        target: _verso_ts.ScriptTarget.ES2022,
                        module: _verso_ts.ModuleKind.CommonJS,
                        strict: false,
                        esModuleInterop: true,
                        skipLibCheck: true,
                        forceConsistentCasingInFileNames: true,
                    },
                    reportDiagnostics: true,
                });
                const errors = (result.diagnostics || [])
                    .filter(d => d.category === _verso_ts.DiagnosticCategory.Error)
                    .map(d => _verso_ts.flattenDiagnosticMessageText(d.messageText, '\n'));
                if (errors.length > 0) {
                    return { error: errors.join('\n') };
                }
                return { code: result.outputText };
            } catch (e) {
                return { error: e.message || String(e) };
            }
        }

        function _verso_handleTranspile(cmd) {
            const result = _verso_transpileTypeScript(cmd.code);
            return { type: 'transpileResult', id: cmd.id, ...result };
        }

        // --- Main loop ---
        const rl = readline.createInterface({ input: process.stdin, crlfDelay: Infinity });

        function _verso_send(obj) {
            process.stdout.write(JSON.stringify(obj) + '\n');
        }

        _verso_send({ type: 'ready' });

        rl.on('line', async (line) => {
            let cmd;
            try { cmd = JSON.parse(line); } catch (_) { return; }

            let response;
            switch (cmd.type) {
                case 'execute':
                    response = await _verso_handleExecute(cmd);
                    break;
                case 'setVariables':
                    response = _verso_handleSetVariables(cmd);
                    break;
                case 'getVariables':
                    response = _verso_handleGetVariables(cmd);
                    break;
                case 'transpile':
                    response = _verso_handleTranspile(cmd);
                    break;
                case 'addModulePath':
                    response = _verso_handleAddModulePath(cmd);
                    break;
                case 'shutdown':
                    process.exit(0);
                    return;
            }
            if (response) _verso_send(response);
        });

        rl.on('close', () => process.exit(0));
        """;
}
