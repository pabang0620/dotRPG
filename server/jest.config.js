/** @type {import('jest').Config} */
module.exports = {
  testEnvironment: 'node',
  roots: ['<rootDir>/test'],
  testMatch: ['**/*.test.ts'],
  transform: { '^.+\\.ts$': ['ts-jest', { tsconfig: 'tsconfig.json' }] },
  globalSetup: '<rootDir>/test/globalSetup.ts',
  setupFiles: ['<rootDir>/test/setupEnv.ts'],
  maxWorkers: 1,
  testTimeout: 30000,
};
