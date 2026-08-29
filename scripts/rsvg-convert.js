#!/usr/bin/env node
// rsvg-convert CLI wrapper using @resvg/resvg-js (napi-rs prebuilt).
// Replaces native rsvg-convert on Windows where it has known binary
// corruption issues on stdout (librsvg#676, #812).
// Supports: rsvg-convert <input.svg> -o <output.png> [-w <width>]

const fs = require('fs');
const { Resvg } = require('@resvg/resvg-js');

const args = process.argv.slice(2);
let output = null;
let inputSvg = null;
let width = null;

for (let i = 0; i < args.length; i++) {
  if ((args[i] === '-o' || args[i] === '--output') && i + 1 < args.length) {
    output = args[i + 1];
    i++;
  } else if ((args[i] === '-w' || args[i] === '--width') && i + 1 < args.length) {
    width = parseInt(args[i + 1], 10);
    i++;
  } else if (!args[i].startsWith('-') && !inputSvg) {
    inputSvg = args[i];
  }
}

if (!inputSvg || !output) {
  console.error('Usage: rsvg-convert <input.svg> -o <output.png> [-w <width>]');
  process.exit(1);
}

const svgBuffer = fs.readFileSync(inputSvg);
const options = {};
if (width) {
  options.fitTo = { mode: 'width', value: width };
}
const resvg = new Resvg(svgBuffer, options);
const pngData = resvg.render().asPng();
fs.writeFileSync(output, pngData);
