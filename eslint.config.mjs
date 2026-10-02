// ESLint for Creatio Classic UI client schemas (AMD modules, ES5, browser globals).
// Only syntax and likely bugs (eslint:recommended); no style rules — formatting follows the
// Creatio code style and is not enforced here.
import js from "@eslint/js";

export default [
  {
    files: ["creatio/**/*.js"],
    ...js.configs.recommended,
    languageOptions: {
      // Classic UI schemas are plain ES5 scripts loaded by RequireJS.
      ecmaVersion: 5,
      sourceType: "script",
      globals: {
        define: "readonly",
        require: "readonly",
        Terrasoft: "readonly",
        Ext: "readonly",
        window: "readonly",
        console: "readonly"
      }
    },
    rules: {
      ...js.configs.recommended.rules,
      // Schema methods often ignore some of the arguments passed by the platform.
      "no-unused-vars": ["error", { "args": "none" }]
    }
  }
];
