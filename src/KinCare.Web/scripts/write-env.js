const fs = require('fs');
const path = require('path');

const targetFile = path.resolve(__dirname, '../src/environments/environment.production.ts');
const apiUrl = process.env.API_BASE_URL || 'http://localhost:5000';

let contents = fs.readFileSync(targetFile, 'utf8');
contents = contents.replace('KINCARE_API_URL_PLACEHOLDER', apiUrl);
fs.writeFileSync(targetFile, contents);
