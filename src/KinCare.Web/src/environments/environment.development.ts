export const environment = {
  production: false,
  apiUrl: 'http://localhost:8080',
  googleMapsApiKey: '',   // Add from console.cloud.google.com → Maps Embed API
  fcmVapidKey: '',        // Add from Firebase console → Project settings → Cloud Messaging → Web Push certificates
  firebaseConfig: {
    apiKey: '',           // Firebase console → Project settings → Your apps → SDK setup
    authDomain: '',
    projectId: '',
    storageBucket: '',
    messagingSenderId: '',
    appId: '',
  },
};
