/** Service가 던지는 에러. 중앙 errorHandler가 { success:false, message, errors:{code,...} }로 바꾼다. */
export class AppError extends Error {
  constructor(
    public readonly status: number,
    message: string,
    public readonly code?: string,
    public readonly extra?: Record<string, unknown>,
  ) {
    super(message);
    this.name = 'AppError';
  }
}
