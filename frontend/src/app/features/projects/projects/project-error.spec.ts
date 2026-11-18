import { HttpErrorResponse } from '@angular/common/http';
import { projectErrorMessage } from './project-error';

describe('projectErrorMessage', () => {
  function response(status: number, error: unknown): HttpErrorResponse {
    return new HttpErrorResponse({ status, error });
  }

  it('explains a duplicate name', () => {
    expect(projectErrorMessage(response(409, { code: 'Project.NameTaken' }))).toBe(
      'Ya tienes un proyecto con ese nombre.',
    );
  });

  it('explains a rename of an archived project', () => {
    expect(projectErrorMessage(response(409, { code: 'Project.Archived' }))).toBe(
      'El proyecto está archivado. Restáuralo antes de cambiar su nombre.',
    );
  });

  it('explains a stale version as a change made elsewhere', () => {
    expect(projectErrorMessage(response(409, { code: 'Project.Conflict' }))).toBe(
      'Otra pestaña o dispositivo cambió este proyecto. Se muestran los datos actuales.',
    );
  });

  it('leaves any other 409 code to the generic description', () => {
    expect(projectErrorMessage(response(409, { code: 'Http.Conflict' }))).toBeNull();
    expect(projectErrorMessage(response(409, 'plain text'))).toBeNull();
  });

  it('ignores errors that are not a 409', () => {
    expect(projectErrorMessage(response(400, { code: 'Project.NameTaken' }))).toBeNull();
    expect(projectErrorMessage(new Error('boom'))).toBeNull();
  });
});
