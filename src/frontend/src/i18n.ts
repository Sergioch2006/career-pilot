import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';
import enUS from './locales/en-US.json';
import ptBR from './locales/pt-BR.json';

const STORAGE_KEY = 'careerpilot_lang';

function getInitialLanguage(): 'pt-BR' | 'en-US' {
  try {
    const saved = localStorage.getItem(STORAGE_KEY);
    if (saved === 'en-US' || saved === 'pt-BR') {
      return saved;
    }
  } catch {
    // Ignora indisponibilidade do localStorage
  }
  return 'pt-BR';
}

const initialLanguage = getInitialLanguage();

void i18n.use(initReactI18next).init({
  resources: {
    'pt-BR': { translation: ptBR },
    'en-US': { translation: enUS },
  },
  lng: initialLanguage,
  fallbackLng: 'pt-BR',
  interpolation: {
    escapeValue: false,
  },
});

i18n.on('languageChanged', (lng) => {
  try {
    localStorage.setItem(STORAGE_KEY, lng);
  } catch {
    // Ignora indisponibilidade do localStorage
  }
  document.documentElement.lang = lng;
});

document.documentElement.lang = initialLanguage;

export default i18n;
