#!/usr/bin/env python3
"""
bot.py - Bot do Discord para gerenciar licenças do JnnBoost.

Comandos disponíveis (só funcionam para quem tem permissão de
Administrador no servidor do Discord):

  !criarlicenca [dias]
      Cria uma nova licença com chave gerada automaticamente.
      Se "dias" não for informado, a licença não expira.

  !renovarlicenca <chave> <dias>
      Renova uma licença existente, estendendo o prazo a partir de hoje.

  !statuslicenca <chave>
      Mostra o estado atual de uma licença (ativa, hwid, expiração,
      tentativas bloqueadas).
"""

import os
import discord
from discord.ext import commands
import aiohttp

DISCORD_BOT_TOKEN = os.environ["DISCORD_BOT_TOKEN"]
API_URL = os.environ.get("API_URL", "http://jnnboost-api:8080")
ADMIN_API_KEY = os.environ["ADMIN_API_KEY"]

intents = discord.Intents.default()
intents.message_content = True

bot = commands.Bot(command_prefix="!", intents=intents)

HEADERS = {"X-Admin-Key": ADMIN_API_KEY, "Content-Type": "application/json"}


def apenas_admin():
    """Restringe o comando a quem tem permissão de Administrador no servidor."""
    async def predicado(ctx):
        return ctx.author.guild_permissions.administrator
    return commands.check(predicado)


@bot.event
async def on_ready():
    print(f"Bot conectado como {bot.user}")


@bot.command(name="criarlicenca")
@apenas_admin()
async def criar_licenca(ctx, dias: int = None):
    payload = {"dias": dias}
    async with aiohttp.ClientSession() as session:
        async with session.post(f"{API_URL}/api/admin/criar-licenca", json=payload, headers=HEADERS) as resp:
            data = await resp.json()

            if resp.status != 200:
                await ctx.send(f"❌ Erro ao criar licença: `{data.get('erro', 'desconhecido')}`")
                return

            expira = data.get("expira_em")
            texto_expira = f"expira em {expira}" if expira else "sem expiração"
            await ctx.send(
                f"✅ Licença criada!\n"
                f"**Chave:** `{data['chave']}`\n"
                f"**Validade:** {texto_expira}"
            )


@bot.command(name="renovarlicenca")
@apenas_admin()
async def renovar_licenca(ctx, chave: str, dias: int):
    payload = {"chave": chave, "dias": dias}
    async with aiohttp.ClientSession() as session:
        async with session.post(f"{API_URL}/api/admin/renovar-licenca", json=payload, headers=HEADERS) as resp:
            data = await resp.json()

            if resp.status != 200:
                await ctx.send(f"❌ Erro ao renovar licença: `{data.get('erro', 'desconhecido')}`")
                return

            await ctx.send(
                f"✅ Licença `{data['chave']}` renovada!\n"
                f"**Nova validade:** expira em {data['expira_em']}"
            )


@bot.command(name="statuslicenca")
@apenas_admin()
async def status_licenca(ctx, chave: str):
    async with aiohttp.ClientSession() as session:
        async with session.get(f"{API_URL}/api/admin/licenca/{chave}", headers=HEADERS) as resp:
            data = await resp.json()

            if resp.status != 200:
                await ctx.send(f"❌ Erro: `{data.get('erro', 'desconhecido')}`")
                return

            status_emoji = "🟢" if data["ativa"] else "🔴"
            await ctx.send(
                f"{status_emoji} **Licença:** `{data['chave']}`\n"
                f"**Ativa:** {data['ativa']}\n"
                f"**HWID:** {data['hwid'] or 'não vinculado'}\n"
                f"**Ativada em:** {data['data_ativacao'] or 'nunca'}\n"
                f"**Expira em:** {data['expira_em'] or 'sem expiração'}\n"
                f"**Tentativas bloqueadas:** {data['tentativas_bloqueadas']}"
            )


@criar_licenca.error
@renovar_licenca.error
@status_licenca.error
async def erro_comando(ctx, error):
    if isinstance(error, commands.CheckFailure):
        await ctx.send("🚫 Você não tem permissão para usar esse comando.")
    elif isinstance(error, commands.MissingRequiredArgument):
        await ctx.send(f"⚠️ Argumento faltando: `{error.param.name}`")
    elif isinstance(error, commands.BadArgument):
        await ctx.send("⚠️ Argumento inválido - confira o formato do comando.")
    else:
        await ctx.send(f"❌ Erro inesperado: `{error}`")


bot.run(DISCORD_BOT_TOKEN)
