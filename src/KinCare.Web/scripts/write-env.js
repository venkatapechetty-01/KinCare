const fs = require('fs');
const path = require('path');

const targetFile = path.resolve(__dirname, '../src/environments/environment.production.ts');
const apiUrl = process.env.API_BASE_URL || 'http://localhost:5000';
const locationIqApiKey = process.env.LOCATIONIQ_API_KEY || '';

let contents = fs.readFileSync(targetFile, 'utf8');
if (contents.includes('KINCARE_API_URL_PLACEHOLDER')) {
  contents = contents.replace('KINCARE_API_URL_PLACEHOLDER', apiUrl);
} else {
  contents = contents.replace(/http:\/\/localhost:5000/g, apiUrl);
}
contents = contents.replace('LOCATIONIQ_API_KEY_PLACEHOLDER', locationIqApiKey);
fs.writeFileSync(targetFile, contents);
