import { HttpErrorResponse } from '@angular/common/http';
import { taskProjectErrorMessage } from './task-project-error';

describe('taskProjectErrorMessage', () => {
  function response(status: number, error: unknown): HttpErrorResponse {
    return new HttpErrorResponse({ status, error });
  }

  it('explains a Task.ProjectArchived 409', () => {
    expect(taskProjectErrorMessage(response(409, { code: 'Task.ProjectArchived' }))).toBe(
      'El proyecto elegido está archivado. Elige otro o deja «Sin proyecto».',
    );
  });

  it('explains a 400 on the ProjectId field as a project that is no longer available', () => {
    expect(
      taskProjectErrorMessage(
        response(400, { code: 'Validation.Failed', errors: { ProjectId: ['Proyecto inválido.'] } }),
      ),
    ).toBe('El proyecto elegido ya no está disponible. Elige otro o deja «Sin proyecto».');
  });

  it('leaves other 400 fields and other 409 codes to the generic description', () => {
    expect(
      taskProjectErrorMessage(response(400, { errors: { Title: ['El título es obligatorio.'] } })),
    ).toBeNull();
    expect(taskProjectErrorMessage(response(409, { code: 'Task.Conflict' }))).toBeNull();
  });

  it('ignores errors that are not an HTTP error response', () => {
    expect(taskProjectErrorMessage(response(400, 'plain text'))).toBeNull();
    expect(taskProjectErrorMessage(new Error('boom'))).toBeNull();
  });
});
