import React, { useEffect, useState } from 'react';
import { createRoot } from 'react-dom/client';
import './style.css';

type Athlete = { stravaAthleteId: number; firstName: string; lastName: string; connectedAt: string; accessTokenExpiresAt: string; scopes: string; requiresReconnect: boolean };
const messages: Record<string,string> = {
  not_configured: 'Strava setup is needed. Follow the repository setup guide, then restart the API.',
  invalid_state: 'This connection attempt expired or was already used. Please try again.',
  access_denied: 'You cancelled the Strava connection. You can try again whenever you are ready.',
  missing_code: 'Strava did not return an authorisation code. Please try again.',
  missing_scope: 'Please allow the requested read permissions to connect your running activity.',
  athlete_not_allowed: 'This prototype is configured for a different Strava athlete.',
  authorization_failed: 'Strava could not authorise this connection. Please try connecting again.',
  reconnect_required: 'Your Strava authorisation needs renewing. Please reconnect.',
  rate_limited: 'Strava is limiting requests. Please try again later.',
  invalid_response: 'Strava returned an unexpected response. Please try again later.',
  strava_unavailable: 'Strava is unavailable right now. Please try again later.'
};
function App() {
  const [athlete, setAthlete] = useState<Athlete|null>(null);
  const [configured, setConfigured] = useState(false);
  const [csrf, setCsrf] = useState('');
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState(() => {
    const error = new URLSearchParams(location.search).get('error');
    return error ? messages[error] ?? 'Connection failed. Please try again.' : '';
  });
  async function load() {
    const sessionResponse = await fetch('/api/session');
    if (!sessionResponse.ok) throw new Error('The application could not load. Please refresh to retry.');
    const session = await sessionResponse.json();
    setConfigured(session.configured); setCsrf(session.csrfToken);
    const response = await fetch('/api/athlete');
    if (response.status === 401) setAthlete(null);
    else if (response.ok) setAthlete(await response.json());
    else throw new Error('Your connection details could not be loaded. Please refresh to retry.');
  }
  useEffect(() => {
    history.replaceState({}, '', '/');
    load().catch(e => setNotice(e.message)).finally(() => setLoading(false));
  }, []);
  async function act(action: 'check'|'logout') {
    setBusy(true); setNotice('');
    try {
      const response = await fetch(action === 'check' ? '/api/strava/check' : '/api/session/logout', {
        method: 'POST', headers: { 'X-CSRF-TOKEN': csrf }
      });
      if (!response.ok) {
        const body = await response.json().catch(() => ({}));
        throw new Error(messages[body.error] ?? 'The request failed. Please refresh and try again.');
      }
      await load();
      if (action === 'check') setNotice('Connection verified with Strava.');
    } catch (e) { setNotice(e instanceof Error ? e.message : 'Something went wrong. Please try again.'); }
    finally { setBusy(false); }
  }
  return <><header><span className="mark" aria-hidden="true">/ /</span> RUN LEAGUE <span className="tag">DEVELOPER PREVIEW</span></header>
    <main><p className="eyebrow">YOUR STRAVA CONNECTION</p>
    {loading ? <p role="status">Loading your connection…</p> : athlete ? <>
      <h1>{athlete.requiresReconnect ? 'Reconnect your account.' : 'You’re connected.'}</h1>
      <section aria-label="Connected athlete"><span className="status">{athlete.requiresReconnect ? 'RECONNECTION NEEDED' : 'STRAVA CONNECTED'}</span>
        <h2>{[athlete.firstName, athlete.lastName].filter(Boolean).join(' ') || 'Strava athlete'}</h2>
        <dl><div><dt>Athlete ID</dt><dd>{athlete.stravaAthleteId}</dd></div><div><dt>Connected</dt><dd>{new Date(athlete.connectedAt + (athlete.connectedAt.endsWith('Z') ? '' : 'Z')).toLocaleDateString()}</dd></div><div><dt>Permissions</dt><dd>{athlete.scopes}</dd></div></dl>
        <div className="actions"><button disabled={busy} onClick={() => act('check')}>{busy ? 'Please wait…' : 'Check connection'}</button><a href="/api/strava/connect">Reconnect with Strava</a></div>
      </section><p className="muted">Your account is ready. Activity importing and the run viewer are coming in the next milestones.</p>
      <button className="secondary" disabled={busy} onClick={() => act('logout')}>Sign out of this browser</button>
    </> : <><h1>Connect your<br/>running activity.</h1><p className="intro">Link your Strava account to get started with RUN LEAGUE.</p>
      <section><h2>Start with Strava</h2><p>Allow read access to your profile and activities. You can review the permissions on Strava before connecting.</p>
      {configured ? <a className="connect" href="/api/strava/connect">Connect with Strava <span aria-hidden="true">↗</span></a> : <p className="setup">Strava is not configured yet. Follow the README setup steps and restart the API.</p>}
      </section></>}
      {notice && <p className="notice" role="status">{notice}</p>}
    </main><footer>RUN LEAGUE <span>Milestone 01 · Account connection</span></footer></>;
}
createRoot(document.getElementById('root')!).render(<React.StrictMode><App/></React.StrictMode>);
