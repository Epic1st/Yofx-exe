/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_YO4X_CONTROL_API_ORIGIN?: string;
  readonly VITE_YO4X_BROKER_ACCOUNT_ID?: string;
  readonly VITE_YO4X_DEPLOYMENT_ID?: string;
  readonly VITE_YO4X_STRATEGY_CORPUS_ID?: string;
  readonly VITE_YO4X_RUNTIME_READINESS_PATH?: string;
  readonly VITE_YO4X_SIGN_IN_URL?: string;
  readonly VITE_YO4X_DEVELOPMENT_IDENTITY_ENABLED?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}

interface Yo4xAuthBridge {
  getAccessToken: () => Promise<string | null>;
  beginLogin?: (intent?: 'sign-in' | 'create-account') => Promise<void>;
}

interface Window {
  __YO4X_AUTH__?: Yo4xAuthBridge;
}
