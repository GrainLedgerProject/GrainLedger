import { ArrowClockwise, WarningOctagon } from '@phosphor-icons/react';
import React, { Component, type ErrorInfo, type ReactNode } from 'react';

interface Props {
  children: ReactNode;
  fallbackTitle?: string;
}

interface State {
  hasError: boolean;
  error: Error | null;
}

export class ErrorBoundary extends Component<Props, State> {
  public override state: State = {
    hasError: false,
    error: null,
  };

  public static getDerivedStateFromError(error: Error): State {
    return { hasError: true, error };
  }

  public override componentDidCatch(error: Error, errorInfo: ErrorInfo) {
    // eslint-disable-next-line no-console
    console.error('ErrorBoundary caught an error:', error, errorInfo);
  }

  private handleReset = () => {
    this.setState({ hasError: false, error: null });
    window.location.reload();
  };

  public override render() {
    if (this.state.hasError) {
      return (
        <div className="min-h-[400px] flex items-center justify-center p-6">
          <div className="max-w-md w-full bg-surface-raised dark:bg-surface-overlay border-2 border-accent-danger/40 rounded-lg p-6 shadow-md text-center space-y-4">
            <div className="w-12 h-12 rounded-full bg-red-100 dark:bg-red-950/50 text-accent-danger flex items-center justify-center mx-auto">
              <WarningOctagon size={28} weight="fill" />
            </div>

            <div className="space-y-1">
              <h2 className="text-lg font-bold text-ink-primary font-serif">
                {this.props.fallbackTitle || 'Đã xảy ra sự cố không mong muốn'}
              </h2>
              <p className="text-xs text-ink-secondary">
                Hệ thống đã cách ly lỗi để bảo toàn tính toàn vẹn của phiên làm việc.
              </p>
            </div>

            {this.state.error && (
              <div className="p-3 rounded bg-surface-base text-left text-xs font-mono text-accent-danger overflow-x-auto max-h-32 border border-ink-tertiary/20">
                {this.state.error.message}
              </div>
            )}

            <div className="pt-2 flex items-center justify-center gap-3">
              <button
                type="button"
                onClick={this.handleReset}
                className="inline-flex items-center gap-2 px-4 py-2 rounded-md bg-accent-primary text-white text-sm font-semibold hover:bg-accent-primary-dim transition-colors shadow-xs"
              >
                <ArrowClockwise size={16} />
                Tải lại giao diện
              </button>
            </div>
          </div>
        </div>
      );
    }

    return this.props.children;
  }
}
