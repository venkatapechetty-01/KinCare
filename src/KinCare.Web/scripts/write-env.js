const fs = require('fs');
const path = require('path');

const targetFile = path.resolve(__dirname, '../src/environments/environment.production.ts');
const apiUrl = process.env.API_BASE_URL || 'http://localhost:5000';
const locationIqApiKey = process.env.LOCATIONIQ_API_KEY || '';
const googleMapsApiKey = process.env.GOOGLE_MAPS_API_KEY || '';
const fcmVapidKey = process.env.FCM_VAPID_KEY || '';
const firebaseApiKey = process.env.FIREBASE_API_KEY || '';
const firebaseAuthDomain = process.env.FIREBASE_AUTH_DOMAIN || '';
const firebaseProjectId = process.env.FIREBASE_PROJECT_ID || '';
const firebaseStorageBucket = process.env.FIREBASE_STORAGE_BUCKET || '';
const firebaseMessagingSenderId = process.env.FIREBASE_MESSAGING_SENDER_ID || '';
const firebaseAppId = process.env.FIREBASE_APP_ID || '';

let contents = fs.readFileSync(targetFile, 'utf8');
if (contents.includes('KINCARE_API_URL_PLACEHOLDER')) {
  contents = contents.replace('KINCARE_API_URL_PLACEHOLDER', apiUrl);
} else {
  contents = contents.replace(/http:\/\/localhost:5000/g, apiUrl);
}
contents = contents.replace('LOCATIONIQ_API_KEY_PLACEHOLDER', locationIqApiKey);
contents = contents.replace('GOOGLE_MAPS_API_KEY_PLACEHOLDER', googleMapsApiKey);
contents = contents.replace('FCM_VAPID_KEY_PLACEHOLDER', fcmVapidKey);
contents = contents.replace('FIREBASE_API_KEY_PLACEHOLDER', firebaseApiKey);
contents = contents.replace('FIREBASE_AUTH_DOMAIN_PLACEHOLDER', firebaseAuthDomain);
contents = contents.replace('FIREBASE_PROJECT_ID_PLACEHOLDER', firebaseProjectId);
contents = contents.replace('FIREBASE_STORAGE_BUCKET_PLACEHOLDER', firebaseStorageBucket);
contents = contents.replace('FIREBASE_MESSAGING_SENDER_ID_PLACEHOLDER', firebaseMessagingSenderId);
contents = contents.replace('FIREBASE_APP_ID_PLACEHOLDER', firebaseAppId);
fs.writeFileSync(targetFile, contents);
