import type { Config } from 'tailwindcss';

const config: Config = {
  content: ['./app/**/*.{ts,tsx}', './components/**/*.{ts,tsx}'],
  theme: {
    extend: {
      colors: {
        nbc: {
          DEFAULT: '#0a4d8c',
          dark: '#063662',
          accent: '#e2231a',
        },
      },
    },
  },
  plugins: [],
};

export default config;
