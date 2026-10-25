import { HttpErrorResponse } from '@angular/common/http';

/**
 * Turns a failed save into a message people can act on. The API answers validation problems with
 * 400 + { errors: { field: ["Mensagem…"] } }; show those messages instead of a generic "check the
 * connection", which sent people looking in the wrong place.
 */
export function saveErrorMessage(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    const errors = error.error?.errors as Record<string, string[]> | undefined;
    if (error.status === 400 && errors) {
      const messages = Object.values(errors).flat().filter(Boolean);
      if (messages.length) return messages.join(' ');
    }
    if (error.status === 404) return 'Este item não existe mais. A lista foi atualizada.';
    if (error.status === 0) return 'Sem conexão com o painel. Verifique a rede e tente de novo.';
    if (error.status >= 500) return 'O painel teve um erro ao salvar. Tente de novo; se continuar, veja os logs da API.';
  }
  return 'Não foi possível salvar. Tente de novo.';
}
