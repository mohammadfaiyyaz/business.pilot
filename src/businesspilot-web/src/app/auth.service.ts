import { Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { AuthResponse } from './models';

const API = '/api';

@Injectable({ providedIn: 'root' })
export class AuthService {
  readonly session = signal<AuthResponse | null>(null);
  constructor(private readonly http: HttpClient) {}
  login(email: string, password: string): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${API}/auth/login`, { email, password }).pipe(tap(value => this.session.set(value)));
  }
  register(name: string, email: string, password: string, organizationName: string): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${API}/auth/register`, { name, email, password, organizationName }).pipe(tap(value => this.session.set(value)));
  }
  token(): string | null { return this.session()?.token ?? null; }
  logout(): void { this.session.set(null); }
}
