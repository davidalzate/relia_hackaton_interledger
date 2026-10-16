import { definePreset } from '@primeuix/themes';
import Aura from '@primeuix/themes/aura';

// Aura con primario cobalto (#2E3192 = 500) para que PrimeNG siga las convenciones del equipo
export const ReliaPreset = definePreset(Aura, {
  semantic: {
    primary: {
      50: '#eef0fb',
      100: '#d9dcf5',
      200: '#b4baeb',
      300: '#8e96df',
      400: '#5f67c4',
      500: '#2e3192',
      600: '#282a7d',
      700: '#212368',
      800: '#1a1c53',
      900: '#14153f',
      950: '#0c0d28',
    },
  },
});
