import 'dotenv/config';
import { ValidationPipe } from '@nestjs/common';
import { NestFactory } from '@nestjs/core';
import { AppModule } from './app/app.module';
async function bootstrap() {
  const app = await NestFactory.create(AppModule);
  app.setGlobalPrefix('api');
  app.useGlobalPipes(new ValidationPipe({ whitelist:true, forbidNonWhitelisted:true, transform:true }));
  app.enableShutdownHooks();
  await app.listen(Number(process.env.PORT ?? 3101), '0.0.0.0');
}
bootstrap().catch(() => { console.error('Lab API could not start; check local configuration.'); process.exitCode=1; });
