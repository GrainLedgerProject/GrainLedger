const messages: Record<string, string> = {
  unverified_email: 'Google did not confirm that your email address is verified. Try another verified Google account.',
  incomplete_identity: 'Google did not return the account details needed to sign in. Please try again.',
  inactive: 'Your GrainLedger account is disabled. Contact your administrator to restore access.',
  external_session: 'Your sign-in session could not be recovered. Start again using the same browser and site address.',
  correlation: 'Your sign-in session expired or its cookie was blocked. Allow cookies for this site and start again.',
  invalid_state: 'Your sign-in request expired or could not be validated. Please start sign-in again.',
  invalid_client: 'Google rejected the application credentials. Your administrator needs to check the OAuth client configuration.',
  invalid_grant: 'Google could not accept this sign-in attempt. Please start sign-in again.',
  access_denied: 'Google sign-in was cancelled or access was denied. Please try again.',
  provider_unavailable: 'GrainLedger could not reach Google. Please try again in a moment.',
};

export function googleLoginError(search: string): string {
  const params = new URLSearchParams(search);
  const reason = params.get('authError');
  if (reason === null) return '';
  const message = Object.hasOwn(messages, reason) ? messages[reason]
    : 'Google sign-in could not be completed. Try again or contact your administrator.';
  const trace = params.get('authTrace');
  // Only display the server's reference format; never interpret arbitrary URL content.
  return trace && /^[a-zA-Z0-9:_-]{1,100}$/.test(trace) ? `${message} Reference: ${trace}` : message;
}
