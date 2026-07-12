// Firebase Cloud Messaging service worker.
// This file must be at the root of the served app (src/ for ng serve,
// or configured in angular.json assets so it copies to dist/).
// Replace the firebaseConfig values with your real project credentials
// (copy from Firebase console → Project settings → Your apps → SDK setup).

importScripts('https://www.gstatic.com/firebasejs/10.12.0/firebase-app-compat.js');
importScripts('https://www.gstatic.com/firebasejs/10.12.0/firebase-messaging-compat.js');

firebase.initializeApp({
  apiKey: self.__FIREBASE_API_KEY__ || '',
  authDomain: self.__FIREBASE_AUTH_DOMAIN__ || '',
  projectId: self.__FIREBASE_PROJECT_ID__ || '',
  storageBucket: self.__FIREBASE_STORAGE_BUCKET__ || '',
  messagingSenderId: self.__FIREBASE_MESSAGING_SENDER_ID__ || '',
  appId: self.__FIREBASE_APP_ID__ || '',
});

const messaging = firebase.messaging();

messaging.onBackgroundMessage((payload) => {
  const { title, body, icon } = payload.notification || {};
  self.registration.showNotification(title || 'KinCare', {
    body: body || '',
    icon: icon || '/assets/icons/icon-192x192.png',
    badge: '/assets/icons/icon-72x72.png',
    data: payload.data,
  });
});
