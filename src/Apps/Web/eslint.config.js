import js from '@eslint/js'
import globals from 'globals'
import reactHooks from 'eslint-plugin-react-hooks'
import reactRefresh from 'eslint-plugin-react-refresh'
import tseslint from 'typescript-eslint'
import { defineConfig, globalIgnores } from 'eslint/config'

const noEffects =
  'Effects are not how this app loads data or reacts to things: load data through the router and the query cache, subscribe with useSyncExternalStore, compute derived values while rendering, and act on events in their handlers.'

export default defineConfig([
  globalIgnores(['dist']),
  {
    files: ['**/*.{ts,tsx}'],
    extends: [
      js.configs.recommended,
      tseslint.configs.recommended,
      reactHooks.configs.flat.recommended,
      reactRefresh.configs.vite,
    ],
    languageOptions: {
      globals: globals.browser,
    },
    rules: {
      'react-refresh/only-export-components': 'off',
    },
  },
  {
    files: ['src/**/*.{ts,tsx}'],
    // shadcn's components and its theme provider keep the few effects they need to sync with the DOM.
    ignores: [
      'src/components/ui/**',
      'src/components/theme-provider.tsx',
      'src/components/use-shortcut.ts',
    ],
    rules: {
      'no-restricted-syntax': [
        'error',
        {
          selector:
            "ImportDeclaration[source.value='react'] > ImportSpecifier[imported.name=/^use(Layout)?Effect$/]",
          message: noEffects,
        },
      ],
      'no-restricted-properties': [
        'error',
        { object: 'React', property: 'useEffect', message: noEffects },
        { object: 'React', property: 'useLayoutEffect', message: noEffects },
      ],
    },
  },
])
