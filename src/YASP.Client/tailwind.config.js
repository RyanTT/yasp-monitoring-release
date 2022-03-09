const { translate } = require('tailwindcss/defaultTheme')
const defaultTheme = require('tailwindcss/defaultTheme')

module.exports = {
    content: [
        './**/*.{razor,html}'
    ],
    theme: {
        extend: {
            fontFamily: {
                sans: ['Inter var', ...defaultTheme.fontFamily.sans],
            },
            animation: {
                peek: 'peek 1s ease-in-out',
            },
            keyframes: {
                peek: {
                    '0%': { transform: 'translateY(0)' },
                    '100%': { transform: 'translateY(5)' },
                }
            }
        }
    },
    plugins: [
        //require('@tailwindcss/forms'),
        //require('@tailwindcss/typography')
    ],
}
