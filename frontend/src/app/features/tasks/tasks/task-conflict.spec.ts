import { HttpErrorResponse } from '@angular/common/http';
import { taskConflictMessage } from './task-conflict';

describe('taskConflictMessage', () => {
  function response(status: number, error: unknown): HttpErrorResponse {
    return new HttpErrorResponse({ status, error });
  }

  it('explains a Task.Conflict 409 as a change made elsewhere', () => {
    expect(taskConflictMessage(response(409, { status: 409, code: 'Task.Conflict' }))).toBe(
      'Otra pestaña o dispositivo cambió esta tarea. Se muestran los datos actuales.',
    );
  });

  it('leaves any other 409 code to the generic description', () => {
    expect(taskConflictMessage(response(409, { status: 409, code: 'Http.Conflict' }))).toBeNull();
  });

  it('ignores errors that are not a 409', () => {
    expect(taskConflictMessage(response(400, { code: 'Task.Conflict' }))).toBeNull();
    expect(taskConflictMessage(new Error('boom'))).toBeNull();
  });
});
