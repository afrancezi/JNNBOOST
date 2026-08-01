#!/usr/bin/env python3
"""
bot.py - Bot do Discord para gerenciar licenças do JnnBoost.

Comandos disponíveis (só para quem tem permissão de Administrador):

  !criarlicenca [dias]
      Cria uma nova licença. Resposta vai para o canal de LOG_CHANNEL_ID.

  !renovarlicenca <chave> <dias>
      Renova uma licença existente. Resposta vai para LOG_CHANNEL_ID.

  !statuslicenca <chave>
      Mostra o estado de uma licença. Resposta vai para STATUS_CHANNEL_ID.

  !licencasativas
      Mostra quantas licenças estão ativas no momento. Resposta vai
      para STATUS_CHANNEL_ID.

Em todos os casos, a mensagem original do comando é apagada do canal
onde foi digitada (para não deixar chaves de licença expostas).
"""

import os
import discord
from discord.ext import commands
import aiohttp

DISCORD_BOT_TOKEN = os.environ["DISCORD_BOT_TOKEN"]
API_URL = os.environ.get("API_URL", "http://jnnboost-api:8080")
ADMIN_API_KEY = os.environ["ADMIN_API_KEY"]

# Canal para onde vão os resultados de criação/renovação de licença.
LOG_CHANNEL_ID = os.environ.get("LOG_CHANNEL_ID")

# Canal para onde vão consultas (status de uma licença, contagem de ativas).
STATUS_CHANNEL_ID = os.environ.get("STATUS_CHANNEL_ID")

intents = discord.Intents.default()
intents.message_content = True

bot = commands.Bot(command_prefix="!", intents=intents)

HEADERS = {"X-Admin-Key": ADMIN_API_KEY, "Content-Type": "application/json"}


def apenas_admin():
    """Restringe o comando a quem tem permissão de Administrador no servidor."""
    async def predicado(ctx):
        return ctx.author.guild_permissions.administrator
    return commands.check(predicado)


async def apagar_mensagem(ctx):
    """Tenta apagar a mensagem do comando original. Falha silenciosamente
    se o bot não tiver permissão (ex.: 'Manage Messages' não concedida)."""
    try:
        await ctx.message.delete()
    except (discord.Forbidden, discord.NotFound):
        pass


async def enviar_para_canal(channel_id, embed: discord.Embed):
    """Envia um embed para o canal configurado."""
    if channel_id:
        canal = bot.get_channel(int(channel_id))
        if canal is not None:
            await canal.send(embed=embed)


@bot.event
async def on_ready():
    print(f"Bot conectado como {bot.user}")


@bot.command(name="criarlicenca")
@apenas_admin()
async def criar_licenca(ctx, dias: int = None):
    await apagar_mensagem(ctx)

    payload = {"dias": dias}
    async with aiohttp.ClientSession() as session:
        async with session.post(f"{API_URL}/api/admin/criar-licenca", json=payload, headers=HEADERS) as resp:
            data = await resp.json()

            if resp.status != 200:
                embed = discord.Embed(
                    title="❌ Erro ao criar licença",
                    description=f"`{data.get('erro', 'desconhecido')}`",
                    color=discord.Color.red()
                )
                await enviar_para_canal(LOG_CHANNEL_ID, embed)
                return

            expira = data.get("expira_em")
            texto_expira = f"expira em {expira}" if expira else "sem expiração"

            embed = discord.Embed(
                title="📝 Licença criada",
                color=discord.Color.green()
            )
            embed.add_field(name="Executado por", value=ctx.author.mention, inline=True)
            embed.add_field(name="Chave", value=f"`{data['chave']}`", inline=True)
            embed.add_field(name="Validade", value=texto_expira, inline=False)
            embed.timestamp = discord.utils.utcnow()
            await enviar_para_canal(LOG_CHANNEL_ID, embed)


@bot.command(name="renovarlicenca")
@apenas_admin()
async def renovar_licenca(ctx, chave: str, dias: int):
    await apagar_mensagem(ctx)

    payload = {"chave": chave, "dias": dias}
    async with aiohttp.ClientSession() as session:
        async with session.post(f"{API_URL}/api/admin/renovar-licenca", json=payload, headers=HEADERS) as resp:
            data = await resp.json()

            if resp.status != 200:
                embed = discord.Embed(
                    title="❌ Erro ao renovar licença",
                    description=f"`{data.get('erro', 'desconhecido')}`",
                    color=discord.Color.red()
                )
                await enviar_para_canal(LOG_CHANNEL_ID, embed)
                return

            embed = discord.Embed(
                title="🔄 Licença renovada",
                color=discord.Color.blue()
            )
            embed.add_field(name="Executado por", value=ctx.author.mention, inline=True)
            embed.add_field(name="Chave", value=f"`{data['chave']}`", inline=True)
            embed.add_field(name="Nova validade", value=f"expira em {data['expira_em']}", inline=False)
            embed.timestamp = discord.utils.utcnow()
            await enviar_para_canal(LOG_CHANNEL_ID, embed)


@bot.command(name="statuslicenca")
@apenas_admin()
async def status_licenca(ctx, chave: str):
    await apagar_mensagem(ctx)

    async with aiohttp.ClientSession() as session:
        async with session.get(f"{API_URL}/api/admin/licenca/{chave}", headers=HEADERS) as resp:
            data = await resp.json()

            if resp.status != 200:
                embed = discord.Embed(
                    title="❌ Erro",
                    description=f"`{data.get('erro', 'desconhecido')}`",
                    color=discord.Color.red()
                )
                await enviar_para_canal(STATUS_CHANNEL_ID, embed)
                return

            cor = discord.Color.green() if data["ativa"] else discord.Color.red()
            embed = discord.Embed(title="🔍 Status da licença", color=cor)
            embed.add_field(name="Consultado por", value=ctx.author.mention, inline=True)
            embed.add_field(name="Chave", value=f"`{data['chave']}`", inline=True)
            embed.add_field(name="Ativa", value=str(data["ativa"]), inline=True)
            embed.add_field(name="HWID", value=data["hwid"] or "não vinculado", inline=False)
            embed.add_field(name="Ativada em", value=str(data["data_ativacao"] or "nunca"), inline=True)
            embed.add_field(name="Expira em", value=str(data["expira_em"] or "sem expiração"), inline=True)
            embed.add_field(name="Tentativas bloqueadas", value=str(data["tentativas_bloqueadas"]), inline=True)
            embed.timestamp = discord.utils.utcnow()
            await enviar_para_canal(STATUS_CHANNEL_ID, embed)


@bot.command(name="revogarlicenca")
@apenas_admin()
async def revogar_licenca(ctx, chave: str):
    await apagar_mensagem(ctx)

    payload = {"chave": chave}
    async with aiohttp.ClientSession() as session:
        async with session.post(f"{API_URL}/api/admin/revogar-licenca", json=payload, headers=HEADERS) as resp:
            data = await resp.json()

            if resp.status != 200:
                embed = discord.Embed(
                    title="❌ Erro ao revogar licença",
                    description=f"`{data.get('erro', 'desconhecido')}`",
                    color=discord.Color.red()
                )
                await enviar_para_canal(LOG_CHANNEL_ID, embed)
                return

            embed = discord.Embed(
                title="🚫 Licença revogada",
                color=discord.Color.dark_red()
            )
            embed.add_field(name="Executado por", value=ctx.author.mention, inline=True)
            embed.add_field(name="Chave", value=f"`{data['chave']}`", inline=True)
            embed.add_field(
                name="Aviso",
                value="Esta licença nunca mais poderá ser usada, mesmo com renovação.",
                inline=False
            )
            embed.timestamp = discord.utils.utcnow()
            await enviar_para_canal(LOG_CHANNEL_ID, embed)


@bot.command(name="licencasativas")
@apenas_admin()
async def licencas_ativas(ctx):
    await apagar_mensagem(ctx)

    async with aiohttp.ClientSession() as session:
        async with session.get(f"{API_URL}/api/admin/licencas-ativas", headers=HEADERS) as resp:
            data = await resp.json()

            if resp.status != 200:
                embed = discord.Embed(
                    title="❌ Erro",
                    description=f"`{data.get('erro', 'desconhecido')}`",
                    color=discord.Color.red()
                )
                await enviar_para_canal(STATUS_CHANNEL_ID, embed)
                return

            embed = discord.Embed(
                title="📊 Licenças ativas",
                description=f"**Total:** {data['total']}",
                color=discord.Color.gold()
            )
            embed.add_field(name="Consultado por", value=ctx.author.mention, inline=False)

            # Mostra até 15 licenças no embed para não estourar o limite do Discord.
            linhas = []
            for item in data["licencas"][:15]:
                expira = item["expira_em"] or "sem expiração"
                linhas.append(f"`{item['chave']}` — expira em {expira}")

            if linhas:
                embed.add_field(name="Licenças", value="\n".join(linhas), inline=False)
                if data["total"] > 15:
                    embed.set_footer(text=f"Mostrando 15 de {data['total']} licenças ativas.")

            embed.timestamp = discord.utils.utcnow()
            await enviar_para_canal(STATUS_CHANNEL_ID, embed)


@criar_licenca.error
@renovar_licenca.error
@revogar_licenca.error
@status_licenca.error
@licencas_ativas.error
async def erro_comando(ctx, error):
    if isinstance(error, commands.CheckFailure):
        # Tenta apagar mesmo em caso de falta de permissão, para não
        # deixar rastro de tentativa de uso indevido no canal público.
        await apagar_mensagem(ctx)
    elif isinstance(error, commands.MissingRequiredArgument):
        await ctx.send(f"⚠️ Argumento faltando: `{error.param.name}`", delete_after=8)
    elif isinstance(error, commands.BadArgument):
        await ctx.send("⚠️ Argumento inválido - confira o formato do comando.", delete_after=8)
    else:
        await ctx.send(f"❌ Erro inesperado: `{error}`", delete_after=8)


bot.run(DISCORD_BOT_TOKEN)