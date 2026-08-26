import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './app/App';
import { readRuntimeConfig } from './app/config/runtimeConfig';
import { FullPageState, ShellLoading } from './app/FullPageState';
import { installDevelopmentAuthBridge } from './auth/developmentOidc';
import './app/styles/tokens.css';
import './app/styles/global.css';

const root = document.getElementById('root');
if (!root) {
  throw new Error('The application root element was not found.');
}

async function bootstrap() {
  let authenticationError: Error | null = null;
  let restoring = false;
  try {
    const config = readRuntimeConfig();
    ({ restoring } = await installDevelopmentAuthBridge(config.developmentOidc));
  } catch (error) {
    authenticationError = error instanceof Error
      ? error
      : new Error('Authentication initialization failed.');
  }

  // While a session restore is navigating, the workspace is neither signed in nor signed out.
  // The loading skeleton stands in for those few frames; rendering the application here would
  // flash the sign-in page at someone who is, in fact, about to be signed in.
  createRoot(root!).render(
    <StrictMode>
      {authenticationError
        ? <FullPageState icon="info" title="Authentication unavailable" detail={authenticationError.message} />
        : restoring
          ? <ShellLoading />
          : <App />}
    </StrictMode>,
  );
}

void bootstrap();
