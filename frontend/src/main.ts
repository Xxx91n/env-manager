import App from './App.svelte'
import { setupI18n } from './lib/i18n'

// Initialize i18n before rendering
setupI18n()

const app = new App({
  target: document.getElementById('app')!,
})

export default app
