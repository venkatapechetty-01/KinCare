export const environment = {
  production: false,
  apiUrl: 'http://localhost:8080',
  googleMapsApiKey: '',   // Add from console.cloud.google.com → Maps Embed API
  locationIqApiKey: 'pk.89fe23b38c9af609e5a82a7498e641ec',  // Reused for Leaflet map tiles on Live Map
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
