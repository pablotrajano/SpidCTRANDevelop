import os
import hashlib
import base64

data = [
    ("CAEDI/DETEC", "Titular", "Marisa Seixas Prata", "P_4269"),
    ("SENET/SERAP/CAINF/DITEC", "Titular", "Leonardo Serra", "P_7916"),
    ("SEMIR/SERAP/CAINF/DITEC", "Titular", "Wilton Souza Alencar", "P_7191"),
    ("CCTI/DECOM", "Titular", "Henrique Fernandes Ferreira", "P_7803"),
    ("CEQUI/DETEC", "Titular", "Matheus Sodré Valverde", "P_7636"),
    ("CGRAF/DEAPA", "Titular", "Jorge Luiz Gusmão da Trindade", "P_6231"),
    ("CIVIL/COENG/DETEC", "Titular", "Cristiano Magalhães de Pinho", "P_6823"),
    ("CLP/DECOM", "Titular", "Aldenir Aurea da Silva", "P_5757"),
    ("CMULHER/DECOM", "Titular", "Valéria Aparecida Olinto Pessoa", "P_5420"),
    ("COALP/DEAPA", "Titular", "Samuel Batista da Silva", "P_2545"),
    ("COAPS/DAS", "Titular", "Valeska Marques de Menezes", "P_7257"),
    ("COAUD/DETEC", "Titular", "Alberto Cesar Souza Almeida", "P_5302"),
    ("COAUD/DETEC", "Substituto", "Iury Bertollo Gomes Porto", "P_7640"),
    ("SEMAT/COAUD/DETEC", "Titular", "Alberto Cesar Souza Almeida", "P_5302"),
    ("LOGFI/COAUS/DITEC", "Titular", "Rodrigo Garcia de Melo", "P_7866"),
    ("SESAU/COAUS/DITEC", "Titular", "Dyrson Rodrigues Alves Junior", "P_6980"),
    ("SATUS/COAUS/DITEC", "Titular", "Samuel Diniz Casimiro", "P_7029"),
    ("COBEC/CEDI", "Titular", "José Raymundo Ribeiro Campos", "P_7759"),
    ("SEDIN/COBIB/CEDI", "Titular", "Janice de Oliveira e Silva Silveira", "P_5033"),
    ("SEACE/COBIB/CEDI", "Titular", "Maurinete dos Santos", "P_6271"),
    ("COCEC/DIREX", "Titular", "Freico Fonseca de Almeida", "P_7091"), # Wait it is Frederico Fonseca de Almeida
    ("COEDI/CEDI", "Titular", "Heitor Luiz Ferreira Rosa", "P_8135"),
    ("COENG/DETEC", "Titular", "Guilherme Pacheco Tavares", "P_7635"),
    ("COENG/DETEC", "Substituto", "Paulo Henrique Soares Marra", "P_7631"),
    ("COGEA/DIREX", "Titular", "Maria Clarice de Siqueira Cabral", "P_6893"),
    ("COHAB/DG", "Titular", "Alexandre Alcides da Costa", "P_6750"),
    ("COLOG/DEMAP", "Titular", "Augusto Marcello Pinto Barbosa", "P_6499"),
    ("COMED - DAS", "Titular", "Tereza Cristina Pereira Lima", "P_6043"),
    ("COPAT/DEMAP", "Titular", "Rafael Rosa de Lima", "P_8234"),
    ("SEREG/COPAT/DEMAP", "Titular", "Rafael Rosa de Lima", "P_8234"),
    ("CORIP/DIREX", "Titular", "Ana Marusia Pinheiro Lima", "P_6490"),
    ("COTEM/DECOM", "Titular", "Gabriela Matsunaga Menezes", "P_7693"),
    ("INST/COENG/DETEC", "Titular", "Paulo Henrique Soares Marra", "P_7631"),
    ("PL/LID", "Titular", "Roberto Mauro Dutra de Freitas", "P_4526"),
    ("PROSAUDE/DAS", "Titular", "Thaissa Carvalho Tavares Moreira", "P_7427"),
    ("PROSAUDE/DAS", "Substituto", "Marleusa Ribeiro de Sousa Carvalho", "P_5603"),
    ("REPUBLICANOS/LID", "Titular", "Cristina Matiko Takemura", "P_7410"),
    ("RESOFI/PRESI", "Titular", "Flávio Shinji Mori", "P_6355"),
    ("RESOFI/PRESI", "Substituto", "Marlon Alves de Sousa Silva", "P_7040"),
    ("SECOP/COALP/DEAPA", "Titular", "Samuel Batista da Silva", "P_2545"),
    ("SEFIS/COPAT/DEMAP", "Titular", "Elizeth Gomes de Lima", "P_7341")
]

# Fix Frederico
for i, d in enumerate(data):
    if d[2].startswith("Freico"):
        data[i] = (d[0], d[1], "Frederico Fonseca de Almeida", d[3])

def get_hash(password_str):
    password = password_str.encode('utf-8')
    salt = os.urandom(16)
    iterations = 10000
    subkey = hashlib.pbkdf2_hmac('sha256', password, salt, iterations, 32)
    output_bytes = bytearray(61)
    output_bytes[0] = 0x01
    output_bytes[1:5] = (1).to_bytes(4, byteorder='big')
    output_bytes[5:9] = iterations.to_bytes(4, byteorder='big')
    output_bytes[9:13] = (16).to_bytes(4, byteorder='big')
    output_bytes[13:29] = salt
    output_bytes[29:61] = subkey
    return base64.b64encode(output_bytes).decode('utf-8')

seen_pontos = set()
lines = []

for centro, tipo, nome, ponto in data:
    ponto = ponto.lower()
    if ponto in seen_pontos:
        # Ignore duplicate/conflicting pontos in SQL generation for now
        continue
    seen_pontos.add(ponto)
    
    perfil = f"Gestor {tipo}"
    pwd_hash = get_hash(ponto)
    
    line = f"    ('{nome}', '', '{ponto}', '{perfil}', '{pwd_hash}', NULL, 1, 0, 0, (SELECT TOP 1 Id FROM CentrosCusto WHERE Nome = '{centro}')),"
    lines.append(line)

print("\n".join(lines))
