import { HttpContextToken } from '@angular/common/http';

export const SKIP_SESSION = new HttpContextToken<boolean>(() => false);
