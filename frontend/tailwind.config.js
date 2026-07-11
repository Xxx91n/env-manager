/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{svelte,js,ts}'],
  darkMode: 'class',
  theme: {
    extend: {
      colors: {
        gray: {
          750: '#2d2d35',
        },
      },
      fontSize: {
        xs: ['12px', '1.4'],
      },
    },
  },
  plugins: [],
}
