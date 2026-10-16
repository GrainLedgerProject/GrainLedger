import { useEffect, useState, type FormEvent } from 'react';
import { anonymousApi, ApiError, loginWithPassword, type RegistrationPending, type User } from './api';

type View = 'login' | 'register' | 'verify' | 'forgot' | 'reset';

function authError(error: unknown, action: View | 'resend') {
  if (!(error instanceof ApiError)) return 'Không thể kết nối đến GrainLedger. Vui lòng thử lại.';
  if (error.status === 503) return action === 'login'
    ? 'Đăng nhập bằng email hiện chưa được cấu hình. Vui lòng liên hệ quản trị viên.'
    : 'Dịch vụ email hiện chưa sẵn sàng. Vui lòng liên hệ quản trị viên rồi thử lại.';
  if (error.status === 401) return 'Email hoặc mật khẩu không đúng. Tài khoản có thể bị khóa tạm sau nhiều lần thử.';
  if (error.status === 409) return 'Email này đã có tài khoản. Hãy đăng nhập hoặc đặt lại mật khẩu.';
  if (error.status === 429) return 'Bạn thao tác quá nhanh. Vui lòng chờ rồi thử lại.';
  if (error.status === 400 && (action === 'verify' || action === 'reset')) return 'Mã xác minh không đúng, đã hết hạn hoặc đã được sử dụng.';
  return error.message;
}

const strongPassword = (value: string) => value.length >= 12 && value.length <= 128
  && /[a-z]/.test(value) && /[A-Z]/.test(value) && /\d/.test(value) && /[^A-Za-z0-9]/.test(value);

export function LocalAuth({ oauthError, connectionError, onAuthenticated, onRetry }: {
  oauthError: string; connectionError: string; onAuthenticated: (user: User) => void; onRetry: () => void;
}) {
  const [view, setView] = useState<View>('login');
  const [email, setEmail] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [password, setPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [otp, setOtp] = useState('');
  const [registrationId, setRegistrationId] = useState('');
  const [cooldown, setCooldown] = useState(0);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');

  useEffect(() => {
    if (cooldown <= 0) return;
    const timer = window.setInterval(() => setCooldown(value => Math.max(0, value - 1)), 1000);
    return () => window.clearInterval(timer);
  }, [cooldown > 0]);

  function changeView(next: View, message = '') {
    setView(next); setError(''); setNotice(message); setPassword(''); setConfirmPassword(''); setOtp('');
  }

  async function login(event: FormEvent) {
    event.preventDefault(); setBusy(true); setError(''); setNotice('');
    try { onAuthenticated(await loginWithPassword(email.trim(), password)); }
    catch (reason) { setError(authError(reason, 'login')); }
    finally { setBusy(false); }
  }

  async function register(event: FormEvent) {
    event.preventDefault(); setError(''); setNotice('');
    if (!strongPassword(password)) { setError('Mật khẩu cần 12–128 ký tự, gồm chữ hoa, chữ thường, số và ký tự đặc biệt.'); return; }
    if (password !== confirmPassword) { setError('Hai mật khẩu chưa khớp.'); return; }
    setBusy(true);
    try {
      const pending = await anonymousApi<RegistrationPending>('/api/auth/register', {
        method: 'POST', body: JSON.stringify({ email: email.trim(), password, displayName: displayName.trim() }),
      });
      setEmail(pending.email); setRegistrationId(pending.registrationId); setCooldown(60);
      changeView('verify', 'Mã gồm 6 chữ số đã được gửi đến email của bạn. Mã có hiệu lực trong 10 phút.');
    } catch (reason) { setError(authError(reason, 'register')); }
    finally { setBusy(false); }
  }

  async function verify(event: FormEvent) {
    event.preventDefault(); setBusy(true); setError('');
    try {
      await anonymousApi<User>('/api/auth/verify-email', { method: 'POST', body: JSON.stringify({ email, registrationId, otp }) });
      changeView('login', 'Email đã được xác minh. Bạn có thể đăng nhập.');
    } catch (reason) { setError(authError(reason, 'verify')); }
    finally { setBusy(false); }
  }

  async function resend() {
    setBusy(true); setError(''); setNotice('');
    try {
      await anonymousApi<RegistrationPending>('/api/auth/resend-verification', { method: 'POST', body: JSON.stringify({ email, registrationId }) });
      setCooldown(60); setNotice('Đã gửi mã mới. Mã cũ không còn hiệu lực.');
    } catch (reason) { setError(authError(reason, 'resend')); }
    finally { setBusy(false); }
  }

  async function forgot(event: FormEvent) {
    event.preventDefault(); setBusy(true); setError('');
    try {
      await anonymousApi<void>('/api/auth/forgot-password', { method: 'POST', body: JSON.stringify({ email: email.trim() }) });
      changeView('reset', 'Nếu email thuộc tài khoản mật khẩu hợp lệ, bạn sẽ nhận được mã đặt lại. Hãy kiểm tra cả thư rác.');
    } catch (reason) { setError(authError(reason, 'forgot')); }
    finally { setBusy(false); }
  }

  async function reset(event: FormEvent) {
    event.preventDefault(); setError('');
    if (!strongPassword(password)) { setError('Mật khẩu mới cần 12–128 ký tự, gồm chữ hoa, chữ thường, số và ký tự đặc biệt.'); return; }
    if (password !== confirmPassword) { setError('Hai mật khẩu chưa khớp.'); return; }
    setBusy(true);
    try {
      await anonymousApi<void>('/api/auth/reset-password', { method: 'POST', body: JSON.stringify({ email: email.trim(), otp, newPassword: password }) });
      changeView('login', 'Đã đổi mật khẩu và đăng xuất các phiên cũ. Hãy đăng nhập bằng mật khẩu mới.');
    } catch (reason) { setError(authError(reason, 'reset')); }
    finally { setBusy(false); }
  }

  return <div className="login-card">
    <span className="eyebrow">YOUR WORKSPACE</span>
    <h2>{view === 'login' ? 'Chào mừng đến GrainLedger' : view === 'register' ? 'Tạo tài khoản' : view === 'verify' ? 'Xác minh email' : view === 'forgot' ? 'Quên mật khẩu' : 'Đặt lại mật khẩu'}</h2>
    {view === 'login' && <p className="muted">Đăng nhập bằng email công việc hoặc Google.</p>}
    {(error || oauthError || connectionError) && <div className="notice error" role="alert">{error || oauthError || connectionError}</div>}
    {notice && <div className="notice" role="status">{notice}</div>}
    {view === 'login' && <>
      <form className="auth-form" onSubmit={login}>
        <label>Email<input type="email" autoComplete="username" value={email} onChange={event => setEmail(event.target.value)} required disabled={busy} /></label>
        <label>Mật khẩu<input type="password" autoComplete="current-password" value={password} onChange={event => setPassword(event.target.value)} required maxLength={128} disabled={busy} /></label>
        <div className="auth-actions"><button type="button" className="text-button" onClick={() => changeView('forgot')} disabled={busy}>Quên mật khẩu?</button><button disabled={busy}>{busy ? 'Đang đăng nhập…' : 'Đăng nhập'}</button></div>
      </form>
      <div className="auth-divider"><span>hoặc</span></div>
      <a className="button google" href="/api/auth/google"><span aria-hidden="true" className="google-mark">G</span> Tiếp tục với Google <span aria-hidden="true">↗</span></a>
      <p className="auth-switch">Chưa có tài khoản? <button className="text-button" onClick={() => changeView('register')} disabled={busy}>Đăng ký</button></p>
      <p className="small muted session-note">Phiên đăng nhập được giữ khi tải lại trang và hết hạn sau 15 phút không hoạt động.</p>
      {connectionError && <button className="text-button" onClick={onRetry}>Thử kết nối lại</button>}
    </>}
    {view === 'register' && <form className="auth-form" onSubmit={register}>
      <label>Họ và tên<input autoComplete="name" value={displayName} onChange={event => setDisplayName(event.target.value)} required maxLength={120} disabled={busy} /></label>
      <label>Email<input type="email" autoComplete="username" value={email} onChange={event => setEmail(event.target.value)} required maxLength={320} disabled={busy} /></label>
      <label>Mật khẩu<input type="password" autoComplete="new-password" value={password} onChange={event => setPassword(event.target.value)} required minLength={12} maxLength={128} disabled={busy} aria-describedby="password-help" /></label>
      <p id="password-help" className="field-help">12–128 ký tự, có chữ hoa, chữ thường, số và ký tự đặc biệt.</p>
      <label>Nhập lại mật khẩu<input type="password" autoComplete="new-password" value={confirmPassword} onChange={event => setConfirmPassword(event.target.value)} required minLength={12} maxLength={128} disabled={busy} /></label>
      <div className="auth-actions"><button type="button" className="secondary" onClick={() => changeView('login')} disabled={busy}>Quay lại</button><button disabled={busy}>{busy ? 'Đang gửi mã…' : 'Đăng ký'}</button></div>
    </form>}
    {view === 'verify' && <form className="auth-form" onSubmit={verify}>
      <p className="muted">Email: <strong>{email}</strong></p>
      <label>Mã xác minh<input className="otp-input" inputMode="numeric" autoComplete="one-time-code" pattern="[0-9]{6}" maxLength={6} value={otp} onChange={event => setOtp(event.target.value.replace(/\D/g, ''))} required disabled={busy} /></label>
      <button disabled={busy || otp.length !== 6}>{busy ? 'Đang xác minh…' : 'Xác minh email'}</button>
      <div className="auth-actions"><button type="button" className="text-button" onClick={() => changeView('register')} disabled={busy}>Đổi thông tin</button><button type="button" className="text-button" onClick={() => void resend()} disabled={busy || cooldown > 0}>{cooldown > 0 ? `Gửi lại sau ${cooldown}s` : 'Gửi lại mã'}</button></div>
    </form>}
    {view === 'forgot' && <form className="auth-form" onSubmit={forgot}>
      <p className="muted">Nhập email tài khoản. Chúng tôi sẽ gửi mã đặt lại nếu tài khoản đủ điều kiện.</p>
      <label>Email<input type="email" autoComplete="username" value={email} onChange={event => setEmail(event.target.value)} required disabled={busy} /></label>
      <div className="auth-actions"><button type="button" className="secondary" onClick={() => changeView('login')} disabled={busy}>Quay lại</button><button disabled={busy}>{busy ? 'Đang gửi…' : 'Gửi mã đặt lại'}</button></div>
    </form>}
    {view === 'reset' && <form className="auth-form" onSubmit={reset}>
      <label>Email<input type="email" autoComplete="username" value={email} onChange={event => setEmail(event.target.value)} required disabled={busy} /></label>
      <label>Mã đặt lại<input className="otp-input" inputMode="numeric" autoComplete="one-time-code" pattern="[0-9]{6}" maxLength={6} value={otp} onChange={event => setOtp(event.target.value.replace(/\D/g, ''))} required disabled={busy} /></label>
      <label>Mật khẩu mới<input type="password" autoComplete="new-password" value={password} onChange={event => setPassword(event.target.value)} required minLength={12} maxLength={128} disabled={busy} /></label>
      <label>Nhập lại mật khẩu mới<input type="password" autoComplete="new-password" value={confirmPassword} onChange={event => setConfirmPassword(event.target.value)} required minLength={12} maxLength={128} disabled={busy} /></label>
      <div className="auth-actions"><button type="button" className="text-button" onClick={() => changeView('forgot')} disabled={busy}>Yêu cầu mã mới</button><button disabled={busy || otp.length !== 6}>{busy ? 'Đang đổi…' : 'Đổi mật khẩu'}</button></div>
      <button type="button" className="text-button auth-cancel" onClick={() => changeView('login')} disabled={busy}>Hủy và quay lại đăng nhập</button>
    </form>}
  </div>;
}
