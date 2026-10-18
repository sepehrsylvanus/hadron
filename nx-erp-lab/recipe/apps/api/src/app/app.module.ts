import { Module } from '@nestjs/common';
import { APP_GUARD } from '@nestjs/core';
import { AppController } from './app.controller';
import { AppService } from './app.service';
import { HealthController } from './health.controller';
import { PrismaService } from './prisma.service';
import { LabGuard } from './lab.guard';
@Module({ controllers:[AppController,HealthController], providers:[AppService,PrismaService,{provide:APP_GUARD,useClass:LabGuard}] })
export class AppModule {}
