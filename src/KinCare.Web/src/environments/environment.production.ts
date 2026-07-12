export const environment = {
  production: true,
  apiUrl: 'http://localhost:5000',  // replaced by CI: sed -i 's|http://localhost:5000|...|g'
  googleMapsApiKey: 'GOOGLE_MAPS_API_KEY_PLACEHOLDER',
  fcmVapidKey: 'FCM_VAPID_KEY_PLACEHOLDER',
  firebaseConfig: {
    apiKey: 'FIREBASE_API_KEY_PLACEHOLDER',
    authDomain: 'FIREBASE_AUTH_DOMAIN_PLACEHOLDER',
    projectId: 'FIREBASE_PROJECT_ID_PLACEHOLDER',
    storageBucket: 'FIREBASE_STORAGE_BUCKET_PLACEHOLDER',
    messagingSenderId: 'FIREBASE_MESSAGING_SENDER_ID_PLACEHOLDER',
    appId: 'FIREBASE_APP_ID_PLACEHOLDER',
  },
};
