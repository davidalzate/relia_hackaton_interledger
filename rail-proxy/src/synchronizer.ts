import type { Logger } from 'pino';

/** Trabajo del sincronizador. Cada vuelta toma del almacén lo que le toca, así sobrevive a un reinicio. */
export interface SyncJob {
  name: string;
  run(): Promise<void>;
}

/** Ciclo de fondo que avanza lo pendiente consultando el riel. Nunca corren dos vueltas a la vez. */
export class Synchronizer {
  private running = false;
  private timer: NodeJS.Timeout | undefined;
  private wake: (() => void) | undefined;
  private loop: Promise<void> | undefined;

  constructor(
    private readonly jobs: SyncJob[],
    private readonly log: Logger,
    private readonly tickMs = 1000,
  ) {}

  start(): void {
    if (this.running) return;
    this.running = true;
    this.loop = this.run();
  }

  async stop(): Promise<void> {
    this.running = false;
    clearTimeout(this.timer);
    this.wake?.();
    await this.loop;
  }

  private async run(): Promise<void> {
    while (this.running) {
      for (const job of this.jobs) {
        try {
          await job.run();
        } catch (error) {
          this.log.error({ err: error, job: job.name }, 'falló una vuelta del sincronizador');
        }
      }
      if (!this.running) break;
      await new Promise<void>((resolve) => {
        this.wake = resolve;
        this.timer = setTimeout(resolve, this.tickMs);
      });
    }
  }
}
