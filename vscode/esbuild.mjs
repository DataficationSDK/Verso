import * as esbuild from "esbuild";

const watch = process.argv.includes("--watch");

/** @type {esbuild.BuildOptions} */
const buildOptions = {
  entryPoints: ["src/extension.ts"],
  bundle: true,
  outfile: "dist/extension.js",
  external: ["vscode"],
  format: "cjs",
  platform: "node",
  target: "node18",
  sourcemap: true,
  minify: !watch,
};

/** @type {esbuild.BuildOptions} */
const rendererBuild = {
  entryPoints: ["src/renderers/mermaidRenderer.ts"],
  bundle: true,
  outfile: "dist/mermaidRenderer.js",
  format: "esm",
  platform: "browser",
  target: "es2020",
  sourcemap: true,
  minify: !watch,
};

if (watch) {
  const ctx = await esbuild.context(buildOptions);
  const rendererCtx = await esbuild.context(rendererBuild);
  await Promise.all([ctx.watch(), rendererCtx.watch()]);
  console.log("Watching for changes...");
} else {
  await Promise.all([
    esbuild.build(buildOptions),
    esbuild.build(rendererBuild),
  ]);
  console.log("Build complete.");
}
