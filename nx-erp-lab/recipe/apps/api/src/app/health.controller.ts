import { Controller, Get, ServiceUnavailableException } from '@nestjs/common';
import { PublicHealth } from './lab.guard';
import { PrismaService } from './prisma.service';
@Controller('health')
export class HealthController {
  constructor(private readonly db: PrismaService) {}
  @Get() @PublicHealth()
  async health() {
    try { await this.db.$queryRaw`SELECT 1`; return { status:'ok', database:'ready', scope:'backend-only-lab' }; }
    catch { throw new ServiceUnavailableException({ status:'not-ready' }); }
  }
}
