// @ts-check
const eslint = require('@eslint/js');
const tseslint = require('typescript-eslint');
const angular = require('@angular-eslint/eslint-plugin');
const angularTemplate = require('@angular-eslint/eslint-plugin-template');
const angularTemplateParser = require('@angular-eslint/template-parser');

module.exports = tseslint.config(
  {
    files: ['**/*.ts'],
    extends: [eslint.configs.recommended, ...tseslint.configs.recommended],
    languageOptions: {
      parser: tseslint.parser,
    },
    plugins: {
      '@angular-eslint': angular,
    },
    rules: {
      ...angular.configs.recommended.rules,
      '@angular-eslint/directive-selector': [
        'error',
        { type: 'attribute', prefix: 'app', style: 'camelCase' },
      ],
      '@angular-eslint/component-selector': [
        'error',
        { type: 'element', prefix: 'app', style: 'kebab-case' },
      ],
      '@typescript-eslint/no-unused-vars': [
        'error',
        { argsIgnorePattern: '^_', varsIgnorePattern: '^_' },
      ],
      '@typescript-eslint/no-explicit-any': 'error',
      '@typescript-eslint/consistent-type-imports': 'warn',
// ADR-009: fechas calendario se serializan siempre desde @core/utils/fechas,
// que opera en hora local. Date.prototype.toISOString() devuelve UTC y rompe
// el cálculo en husos al oeste de UTC entre 19:00 y 23:59 hora local.
// Cubre tanto notación de punto (`d.toISOString()`) como de corchetes
// (`d['toISOString']()`), porque el AST los modela distinto:
//   - punto:   property es Identifier → match por property.name
//   - corchete: property es Literal   → match por property.value
// Escape hatch: // eslint-disable-next-line no-restricted-syntax + comentario.
'no-restricted-syntax': [
  'error',
  {
    selector:
      "MemberExpression[property.name='toISOString'], MemberExpression[property.value='toISOString']",
    message:
      'Prohibido .toISOString() fuera de @core/utils/fechas. Usá fechaLocalAISO/lunesDe/domingoDe/hoyEnISO/haceDiasISO/sumarDiasISO. Ver ADR-009.',
  },
],
    },
  },
  {
    files: ['**/*.html'],
    languageOptions: {
      parser: angularTemplateParser,
    },
    plugins: {
      '@angular-eslint/template': angularTemplate,
    },
    rules: {
      ...angularTemplate.configs.recommended.rules,
      ...angularTemplate.configs.accessibility.rules,
    },
  },
  {
    ignores: ['dist/', '.angular/', 'node_modules/', 'coverage/'],
  },
);
