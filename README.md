# CVMParser #
### v1.0, André Ramos, 2022-02-19 ###

**Objetivo:** extrair dados de cotas de fundos de investimentos a partir dos arquivos mensais disponíveis na CVM.
**Status:** funcionalidade básica implementada.

## Para compilação ##
Desenvolvido no Visual Studio Community 2022, C#10, Console

## Instruções para teste: ##
* baixar todos os arquivos de http://dados.cvm.gov.br/dados/FI/DOC/INF_DIARIO/DADOS/
* colocá-los em c:\temp
* na mesma pasta, c:\temp, criar um arquivo de texto e escrever, em cada linha, os CNPJs de interesse no formato 12.345.678/0001-23
* executar utilitário da forma
* `CVMParser < c:\temp\arq_cnpj.txt`
* gerará o arquivo de saída _DADOS_FILTRADOS.csv que pode ser aberto e tratado diretamente no Excel

## Exemplo completo: ##
`CVMParser 2017 1 2021 12 c:\dados_cvm c:\saida < c:\pasta\cnpj.txt`
Nesse caso,
* datas tratadas: 2017-Jan até 2021-Dez (obs.: por enquanto, ano mínimo = 2017)
* arquivos da CVM devem estar armazenados em c:\dados_cvm
* arquivo de saída _DADOS_FILTRADOS.csv será escrito na pasta c:\saida
* o caminho completo do arquivo com os CNPJs sempre deve ser informado
