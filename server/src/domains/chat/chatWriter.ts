// 채팅 쓰기 큐: 프로세스 안에서 한 번에 하나씩 INSERT -> 전달. id 순서가 커밋 순서이자 전달 순서가 된다.
// 서버가 여러 대가 되면 INSERT 앞에 pg_advisory_xact_lock으로 같은 직렬화를 DB에서 얻는다(이 인터페이스 뒤에서 바꾼다).
export interface ChatWriter {
  run<T>(job: () => Promise<T>): Promise<T>;
  /** 큐가 빌 때까지 기다린다(서버 종료) */
  drain(): Promise<void>;
}

export class SerialChatWriter implements ChatWriter {
  private tail: Promise<unknown> = Promise.resolve();

  run<T>(job: () => Promise<T>): Promise<T> {
    const result = this.tail.then(job, job);
    this.tail = result.catch(() => undefined);
    return result;
  }

  async drain(): Promise<void> {
    await this.tail;
  }
}

let writer: ChatWriter = new SerialChatWriter();
export const getChatWriter = (): ChatWriter => writer;
export const setChatWriter = (w: ChatWriter): void => {
  writer = w;
};
