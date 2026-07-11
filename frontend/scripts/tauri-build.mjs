// Detect the Rust host triple and pass it to `tauri build -- --target <triple>`.
// This ensures Tauri's bundler looks in the same directory where cargo
// actually places the binary (target/<triple>/release/ instead of target/release/),
// which matters on hosts whose default triple is not the "plain" host
// (e.g. x86_64-pc-windows-gnu with MinGW).
import { execSync } from "node:child_process";
import { exit } from "node:process";

let triple = "";
try {
  const out = execSync("rustc -vV", { encoding: "utf-8" });
  const m = out.match(/host:\s*(\S+)/);
  if (m) triple = m[1];
} catch {
  // rustc not found - let tauri build fail naturally
}

const args = ["build"];
if (triple) {
  args.push("--target", triple);
}
// Forward any extra CLI args after `--`
const userArgs = process.argv.slice(2);
if (userArgs.length) {
  args.push("--", ...userArgs);
}

const { spawn } = await import("node:child_process");
const child = spawn("npx", ["tauri", ...args], {
  stdio: "inherit",
  shell: true,
});
child.on("exit", (code) => exit(code ?? 1));
