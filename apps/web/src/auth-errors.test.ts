import { describe, expect, it } from 'vitest';
import { googleLoginError } from './auth-errors';

describe('Google sign-in failures', () => {
  it('explains a disabled account and includes its backend log reference', () => {
    expect(googleLoginError('?authError=inactive&authTrace=0ABC:0001'))
      .toBe('Your GrainLedger account is disabled. Contact your administrator to restore access. Reference: 0ABC:0001');
  });
  it('distinguishes rejected verification from a cookie failure', () => {
    expect(googleLoginError('?authError=unverified_email')).toContain('email address is verified');
    expect(googleLoginError('?authError=correlation')).toContain('cookie was blocked');
  });
  it('handles unknown reasons without reflecting arbitrary URL content', () => {
    expect(googleLoginError('?authError=toString&authTrace=%3Cscript%3E')).toBe('Google sign-in could not be completed. Try again or contact your administrator.');
    expect(googleLoginError('')).toBe('');
  });
});
