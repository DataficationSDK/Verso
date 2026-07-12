import * as fs from "fs";
import * as os from "os";
import * as path from "path";
import { runTests } from "@vscode/test-electron";

async function main(): Promise<void> {
  try {
    // The folder containing the Extension Manifest package.json
    const extensionDevelopmentPath = path.resolve(__dirname, "../../");

    // The path to the test runner script
    const extensionTestsPath = path.resolve(__dirname, "./suite/index");

    // VS Code creates a unix socket inside the user data dir; macOS caps socket
    // paths at 103 chars, so the default dir under a deep repo path fails to
    // launch. A short temp dir keeps the socket path legal everywhere.
    const userDataDir = fs.mkdtempSync(path.join(os.tmpdir(), "verso-vsc-test-"));

    // Download VS Code, unzip it, and run the integration tests
    await runTests({
      extensionDevelopmentPath,
      extensionTestsPath,
      launchArgs: ["--disable-extensions", "--user-data-dir", userDataDir],
    });
  } catch (err) {
    console.error("Failed to run tests:", err);
    process.exit(1);
  }
}

main();
