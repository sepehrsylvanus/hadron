import { Component, type ErrorInfo, type ReactNode } from 'react'
import i18n, { languageDirection, normalizeLanguage } from './i18n'

type State = { failed: boolean }

export default class ApplicationErrorBoundary extends Component<{ children: ReactNode }, State> {
  state: State = { failed: false }

  static getDerivedStateFromError(): State {
    return { failed: true }
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    // Keep diagnostic detail in the browser console for support; never render stack traces to users.
    console.error('Hadron ERP UI boundary caught an unexpected error.', error, info)
  }

  render() {
    if (!this.state.failed) return this.props.children
    const language = normalizeLanguage(i18n.resolvedLanguage ?? i18n.language)
    return <main className="application-failure" dir={languageDirection(language)} lang={language}>
      <section role="alert" aria-labelledby="application-failure-title">
        <span className="application-failure__mark" aria-hidden="true">!</span>
        <div>
          <h1 id="application-failure-title">{i18n.t('common.unexpectedErrorTitle')}</h1>
          <p>{i18n.t('common.unexpectedErrorDescription')}</p>
          <div className="application-failure__actions">
            <button className="button button--primary" type="button" onClick={() => window.location.reload()}>{i18n.t('common.reload')}</button>
            <button className="button button--secondary" type="button" onClick={() => { window.location.href = '/' }}>{i18n.t('shell.home')}</button>
          </div>
        </div>
      </section>
    </main>
  }
}
