/**
 * Cookie Utilities for Secure Session & Token Management
 * Stores accessToken and refreshToken in browser cookies with SameSite=Lax and Path=/
 */

export const COOKIE_KEYS = {
  ACCESS_TOKEN: 'grainledger_access_token',
  REFRESH_TOKEN: 'grainledger_refresh_token',
  USER: 'grainledger_user',
  USER_SESSION: 'grainledger_user_session',
} as const;

/**
 * Set a cookie with specified expiration, path, and secure attributes.
 * If days is undefined or 0, sets a session cookie (removed when browser closes).
 */
export function setCookie(
  name: string,
  value: string,
  days?: number,
  path = '/'
): void {
  if (typeof document === 'undefined') return;

  const isSecure = typeof window !== 'undefined' && window.location.protocol === 'https:';
  const cookieParts = [
    `${encodeURIComponent(name)}=${encodeURIComponent(value)}`,
    `path=${path}`,
    'SameSite=Lax',
  ];

  if (typeof days === 'number' && days > 0) {
    const expires = new Date();
    expires.setTime(expires.getTime() + days * 24 * 60 * 60 * 1000);
    cookieParts.push(`expires=${expires.toUTCString()}`);
  }

  if (isSecure) {
    cookieParts.push('Secure');
  }

  document.cookie = cookieParts.join('; ');
}

/**
 * Get cookie value by name
 */
export function getCookie(name: string): string | null {
  if (typeof document === 'undefined') return null;

  const cookies = document.cookie ? document.cookie.split('; ') : [];
  const encodedName = encodeURIComponent(name) + '=';

  for (const c of cookies) {
    if (c.indexOf(encodedName) === 0) {
      try {
        return decodeURIComponent(c.substring(encodedName.length));
      } catch {
        return c.substring(encodedName.length);
      }
    }
  }

  return null;
}

/**
 * Delete a cookie by expiring it immediately
 */
export function deleteCookie(name: string, path = '/'): void {
  if (typeof document === 'undefined') return;
  document.cookie = `${encodeURIComponent(name)}=; expires=Thu, 01 Jan 1970 00:00:00 GMT; path=${path}; SameSite=Lax`;
  document.cookie = `${encodeURIComponent(name)}=; expires=Thu, 01 Jan 1970 00:00:00 GMT; SameSite=Lax`;
}

/**
 * Clear all authentication-related cookies
 */
export function clearAuthCookies(): void {
  deleteCookie(COOKIE_KEYS.ACCESS_TOKEN);
  deleteCookie(COOKIE_KEYS.REFRESH_TOKEN);
  deleteCookie(COOKIE_KEYS.USER);
  deleteCookie(COOKIE_KEYS.USER_SESSION);
}
