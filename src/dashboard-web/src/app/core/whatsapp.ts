import { HttpClient } from '@angular/common/http';
import { WhatsAppMessage } from './models';

/**
 * Opens WhatsApp with the shopping list ready to send. Call it straight from a click handler: the
 * window is opened synchronously and pointed at wa.me once the text arrives, otherwise browsers
 * treat it as a popup. Resolves false if the message couldn't be prepared.
 */
export function openShoppingListInWhatsApp(http: HttpClient): Promise<boolean> {
  const win = window.open('', '_blank');
  return new Promise((resolve) =>
    http.get<WhatsAppMessage>('/api/shopping/whatsapp').subscribe({
      next: (m) => {
        if (win) win.location.href = m.url;
        else location.href = m.url;
        resolve(true);
      },
      error: () => {
        win?.close();
        resolve(false);
      },
    }),
  );
}
