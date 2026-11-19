import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { safeReturnUrl } from './return-url';

@Component({ template: '' })
class Blank {}

describe('returnUrl regressions', () => {
  it.each([
    '//evil.com',
    '/\\evil.com',
    'https://evil.com',
    'javascript:alert(1)',
    'JAVASCRIPT:alert(1)',
    '%2F%2Fevil.com',
    ' //evil.com',
    '\t//evil.com',
    '\\\\evil.com',
    'http:/evil.com',
    '',
    null,
    undefined,
  ])('rejects %j', (value) => {
    expect(safeReturnUrl(value as string)).toBeNull();
  });

  it.each(['/', '/documentos?estado=1', '/a/b#c'])('keeps internal %j', (value) => {
    expect(safeReturnUrl(value)).toBe(value);
  });

  it.each([
    '/%2F%2Fevil.com',
    '/\t/evil.com',
    '/..//evil.com',
    '/%5Cevil.com',
    '/./\\evil.com',
    '/\n/evil.com',
    '/ /evil.com',
  ])('accepted value %j stays on the panel origin after navigateByUrl', async (value) => {
    TestBed.configureTestingModule({
      providers: [provideRouter([{ path: '**', component: Blank }])],
    });
    const router = TestBed.inject(Router);
    const before = window.location.origin;
    const accepted = safeReturnUrl(value);
    if (accepted === null) {
      return;
    }
    await router.navigateByUrl(accepted).catch(() => false);
    expect(window.location.origin).toBe(before);
    expect(window.location.pathname.startsWith('//')).toBe(false);
    expect(new URL(router.url, 'https://panel.test').origin).toBe('https://panel.test');
  });
});
