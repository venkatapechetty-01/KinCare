import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';

@Injectable({ providedIn: 'root' })
export class DeviceService {
  private readonly api = `${environment.apiUrl}/api`;

  constructor(private http: HttpClient) {}

  async registerFcmToken(): Promise<void> {
    try {
      const permission = await Notification.requestPermission();
      if (permission !== 'granted') return;

      if (!('serviceWorker' in navigator)) return;

      const registration = await navigator.serviceWorker.register('/firebase-messaging-sw.js');

      // Dynamically import Firebase Messaging to avoid breaking the app
      // when Firebase is not configured (local dev without credentials).
      const { getMessaging, getToken } = await import('firebase/messaging');
      const { initializeApp } = await import('firebase/app');

      const firebaseConfig = (environment as any).firebaseConfig;
      if (!firebaseConfig?.apiKey) return;

      const app = initializeApp(firebaseConfig);
      const messaging = getMessaging(app);

      const token = await getToken(messaging, {
        vapidKey: (environment as any).fcmVapidKey,
        serviceWorkerRegistration: registration,
      });

      if (!token) return;

      await this.http
        .post(`${this.api}/devices/register`, {
          fcmToken: token,
          deviceName: 'web',
        })
        .toPromise();
    } catch {
      // FCM registration is best-effort — never block login on this
    }
  }
}
