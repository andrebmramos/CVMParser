using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using FundosParser.Modelos;
using static FundosParser.Core.ParserOptions;
using System.Text;

namespace FundosParser.Core;


public class ParserCore
{
        
    // Injetados no ctor
    private readonly ParserOptions _opts;
    private readonly List<string> _buscar;

    // Outros
    private List<RegistroCotas>     _cotas = new();      // informação principal das cotas. Obtenção em Parse
    private List<RegistroPresenca>? _cachePresencas; // cache das datas quando cada fundo de interesse se faz presente

    public ParserCore(ParserOptions opts, List<string> buscar)
    {
        _opts=opts;
        _buscar=buscar;
    }


    // Execução do comando
    public void Processar()
    {
        switch (_opts.Cmd)
        {
            case Comando.Help:  // Comando padrão, imprime modo de uso    
                Usage();
                break;
            case Comando.Parametros:
                MostrarParametros();
                break;
            case Comando.Processar:
            case Comando.Parse: // Processa arquivos da CVM e escreve arquivo filtrado
                ParsePeriodo();
                if (_opts.EscreverSaida)
                {
                    System.Console.WriteLine("- Escrevendo saída em forma de vetores");
                    EscreverNovoArquivo();
                    // System.Console.WriteLine("- Escrevendo saída em dupla tabela");
                    // EscreverTabelonas();
                }
                else
                {
                    Console.WriteLine("- Ignorando escrita do arquivo de saída");
                }
                break;
            case Comando.Cache: // Constrói cache de presenças; REQUER NOME DE ARQUIVO, parâmetro -cache
                if (string.IsNullOrEmpty(_opts.NomeArquivoCacheDePresencas))
                {
                    Console.WriteLine("### É preciso informar nome para o arquivo de cache a ser salvo com o parâmetro -cache=nome");
                }
                else
                {
                    ConstruirESalvarCacheDePresencas();
                }
                break;
            case Comando.MostraCache: // Lê e exibe cache de presenças; REQUER NOME DE ARQUIVO, parâmetro -cache
                if (string.IsNullOrEmpty(_opts.NomeArquivoCacheDePresencas))
                {
                    Console.WriteLine("### É preciso informar nome para o arquivo de cache a ser lido com o parâmetro -cache=nome");
                }
                else
                {
                    LerCacheDePresencasDeArquivo();
                    MostrarCacheDePresencas();
                }
                break;
            case Comando.Test: 
                ParsePeriodo();
                if (_opts.EscreverSaida)
                {
                    System.Console.WriteLine("- Escrevendo saída em forma de vetores");
                    EscreverNovoArquivo();
                    System.Console.WriteLine("- Escrevendo saída em dupla tabela");
                    EscreverTabelonas();
                }
                else
                {
                    Console.WriteLine("- Ignorando escrita do arquivo de saída");
                }
                break;
            default:
                throw new ArgumentException($"### Comando {_opts.Cmd} não implementado.");
        }
    }
    

    private void EscreverTabelonas()
    {
        // Tratamento: se não foi especificado path de escrita, usar mesmo de leitura
        string path;
        if (_opts.PathEscrita=="")
        {
            path = _opts.PathLeitura;
        }
        else
        {
            path= _opts.PathEscrita;
        }

        // Dois arquivos simultaneos, cotas e variações diárias
        using var wcotas  = new StreamWriter($@"{path}{_opts.NomeArquivoFinal}_cotas.csv");     // CUIDADO, \
        using var wvardia = new StreamWriter($@"{path}{_opts.NomeArquivoFinal}_vardia.csv"); 
        
        // Listas de CNPJs e Datas
        var cnpjs = _cotas.Select(c => c.Cnpj).Distinct().OrderBy(i=>i).ToList();
        var datas = _cotas.Select(c => c.Data).Distinct().OrderBy(i=>i).ToList();

        // Auxiliares
        RegistroCotas? rcota;                 // um único "registro de cotas" (record com data, cnpj, cota, num cotistas)
        double?[] cotahoje, cotaontem;        // vetores das cotas de todos os fundos nas data de hoje e ontem para calcular variação diária
        cotahoje  = new double?[cnpjs.Count]; // preciso inicializar o vetor "hoje" (**)

        // Geração da 1a linha (cabeçalho) dos 2 arquivos, onde em ambos se lê "Data" e os CNPJs
        wcotas.Write("Data");
        wvardia.Write("Data");
        foreach (var cnpj in cnpjs)
        {
            wcotas.Write($";{cnpj}");
            wvardia.Write($";{cnpj}");
        }
        wcotas.Write("\n");  
        wvardia.Write("\n");   

        // Gera linhas, data por data, com cotas e variações diárias
        foreach (var data in datas)
        {
            // Primeiro valor em ambos os arquivos é a própria data
            wcotas.Write(data);
            wvardia.Write(data);
            // Vetores auxiliares: "ontem = hoje", "hoje será recriado". 
            // Na primeira interação, copia-se o vetor vazio criado em (**), dispensando tratamento especial
            cotaontem = cotahoje;
            cotahoje  = new double?[cnpjs.Count];
            // Loop por todos os CNPJs, indexados por i
            for(var i=0; i<cnpjs.Count; i++)
            {
                var cnpj = cnpjs[i];
                rcota = _cotas.SingleOrDefault(c => c.Data.Equals(data) && c.Cnpj.Equals(cnpj)); // ### PONTO CRÍTICO. PRECISO OTIMIZAR!
                if (rcota is not null) // caso registro existente (não nulo)
                {
                    double cota = rcota.Cota;     // armazeno a cota para uso imediato
                    cotahoje[i] = cota;           // armazeno cópia no vetor, que será usada como "cota de ontem" na pŕoxima iteração
                    wcotas.Write($";{cota}");     // escrevo cota no arquivo de cotas
                    if (cotaontem[i] is not null) // calculo e escrevo variação diária apenas se valor "de ontem" não nulo, senão escrevo ";"
                    {
                        wvardia.Write($";{cota/cotaontem[i]-1}");
                    }
                    else
                    {
                        wvardia.Write(";");
                    }
                }   
                else // caso de registro nulo (inexistente), escrever apenas ";"
                {
                    wcotas.Write(";");
                    wvardia.Write(";");
                }
            }
            // Fim da linha nos 2 arquivos de saída
            wcotas.Write("\n"); 
            wvardia.Write("\n"); 
        }
    }


    // Funções relativas ao cache de presenças
    private void ConstruirESalvarCacheDePresencas()
    {
        Console.WriteLine($"> Construindo cache de presenças, buscando {_buscar.Count} fundos");
        
        // Verifica se foi informado nome para arquivo, senão, cancela processamento
        if (string.IsNullOrEmpty(_opts.NomeArquivoCacheDePresencas))
        {
            throw new Exception("### Não foi informado nome para o arquivo cache de presenças");
        }

        // Resultado
        List<RegistroPresenca> result = new();
        List<string> conhecidos = new();
        
        // Contadores
        int contaDescartesTotal = 0;
        int contaNovosTotal = 0;
        int contaArquivosProcessadosTotal = 0;

        // Cronômetro maior
        var watchTotal = new System.Diagnostics.Stopwatch();
        watchTotal.Start();

        // Loop maior
        for (int ano = _opts.AnoInicial; ano <= _opts.AnoFinal; ano++)
        {
            for (int mes = (ano == _opts.AnoInicial ? _opts.MesInicial : 1);
                     mes <= (ano == _opts.AnoFinal ? _opts.MesFinal : 12); mes++)
            {
                // Contadores auxiliares
                int contaDescartes = 0;
                int contaNovos = 0;

                // Identifico arquivo CSV
                string fileName = $@"{_opts.PathLeitura}inf_diario_fi_{ano:0000}{mes:00}.csv"; // CUIDADO, \
                Console.Write($"> Buscando CNPJs em {fileName}...");

                // Crio recursos
                using (var reader = new StreamReader(fileName))
                using (var csv = new CsvReader(reader,
                                               new CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = ";" }))
                {
                    // Leitura do cabeçalho exige esses dois passos
                    csv.Read(); csv.ReadHeader();
                    // Cronômetro menor (leitura de único arquivo)
                    var watcArquivo = new System.Diagnostics.Stopwatch();
                    watcArquivo.Start();
                    // Loop do arquivo
                    string cnpj_anterior = "";
                    while (csv.Read())
                    {
                        string cnpj = csv.GetField(HEADER_Cnpj);
                        if (cnpj != cnpj_anterior && _buscar.Contains(cnpj))
                        {
                            // nesse caso, achei linha com novo cnpj. Atualizo anterior e processo:
                            cnpj_anterior = cnpj;
                            if (conhecidos.Contains(cnpj))  // obs.: trabalhar direto nba lista de resultados é muito menos eficiente: (result.Exists(r => r.Cnpj==cnpj))
                            {
                                // CNPJ já estava identificado: pulo linhas para ganhar tempo e continuo
                                contaDescartes++;
                                continue;
                            }
                            else 
                            {
                                // CNPJ não estava identificado: crio novo registro da sua presença a partir deste mês
                                contaNovos++;
                                conhecidos.Add(cnpj);
                                var rp = new RegistroPresenca(cnpj, ano, mes);
                                result.Add(new RegistroPresenca(cnpj, ano, mes));
                            }   
                        }
                        else
                        {
                            contaDescartes++;
                        }

                    }
                    watcArquivo.Stop();
                    Console.WriteLine($"concluído; Tempo: {watcArquivo.Elapsed}, Encontrados {contaNovos}, Descartados {contaDescartes}");
                    contaDescartesTotal += contaDescartes;
                    contaNovosTotal += contaNovos;
                    contaArquivosProcessadosTotal++;
                    if (contaNovosTotal>=_buscar.Count)
                    {
                        // Caso já tenha encontrado todos os fundos, não preciso
                        // prosseguir e uso goto para sair dos loops aninhados
                        goto TodosEncontrados;
                    }
                }
            }
        }
    TodosEncontrados:
        watchTotal.Stop();
        _cachePresencas = result;
        Console.WriteLine($"> Concluída busca em {contaArquivosProcessadosTotal} arquivos; Tempo: {watchTotal.Elapsed}, Encontrados {contaNovosTotal}; Descartadas {contaDescartesTotal} leituras");
        SalvarCacheDePresencas();
    }

    private void MostrarCacheDePresencas()
    {
        if (_cachePresencas != null)
        {
            Console.WriteLine("> Registro de Presenças:");
            foreach (var item in _cachePresencas)
            {
                Console.WriteLine($"> {item}");
            }
        }
        else
        {
            Console.WriteLine("> Cache de presenças vazio.");
        }
    }

    private void SalvarCacheDePresencas()
    {
        if (string.IsNullOrEmpty(_opts.NomeArquivoCacheDePresencas))
        {
            throw new Exception("### Não foi informado nome para o arquivo cache de presenças");
        }
        Console.WriteLine("> Salvando arquivo de presenças:");
        using (var writer = new StreamWriter($@"{_opts.PathLeitura}{_opts.NomeArquivoCacheDePresencas}.csv"))// CUIDADO, \
        using (var csv = new CsvWriter(writer, CultureInfo.GetCultureInfo("pt-BR")))  // pt-BR para melhor tratamento no Excel
        {
            csv.WriteRecords(_cachePresencas);
        }
    }

    private void LerCacheDePresencasDeArquivo()
    {       
        using (var reader = new StreamReader($@"{_opts.PathLeitura}{_opts.NomeArquivoCacheDePresencas}.csv"))// CUIDADO, \
        using (var csv = new CsvReader(reader,
                                       new CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = ";" }))
            _cachePresencas = csv.GetRecords<RegistroPresenca>().ToList();
    }


    // Funções principais  
    private void ParsePeriodo()
    {
        // Verificar se datas estão válidas
        if (!_opts.ValidarPeriodo())
        {
            throw new ArgumentException("Datas fornecidas inválidas");
        }
        // Utilizara cache se foi informado arquivo, senão desprezará
        bool utilizarCacheDePresencas = _opts.NomeArquivoCacheDePresencas != "";
        // Inicio cronômetro antes do loop
        var watch = new System.Diagnostics.Stopwatch();
        watch.Start();
        // Loop principal
        int mesesProcessados = 0;
        for (int ano = _opts.AnoInicial; ano <= _opts.AnoFinal; ano++)
        {
            for (int mes = (ano == _opts.AnoInicial ? _opts.MesInicial : 1);
                     mes <= (ano == _opts.AnoFinal ? _opts.MesFinal : 12); mes++)
            {
                if (utilizarCacheDePresencas)
                {
                    ParseAnoMesComCache(ano, mes, _buscar, _cotas);
                }
                else 
                {
                    ParseAnoMes(ano, mes, _buscar, _cotas);
                }
                mesesProcessados++;
            }
        }
        // Fim
        watch.Stop();
        Console.WriteLine($"> Processados {mesesProcessados} arquivos, tempo: {watch.Elapsed}");        
    }
    
    private void ParseAnoMes(int ano, int mes, List<string> buscar, List<RegistroCotas> registros)
    {
        // Identifico arquivo de dados da CVM do respectivo ano e mês
        string fileName = $@"{_opts.PathLeitura}inf_diario_fi_{ano:0000}{mes:00}.csv";// CUIDADO, \
        Console.Write($"> Processando arquivo {fileName}...");

        // Variáveis auxiliares para tratar quantos CNPJs foram identificados
        int contaAlvosEncontrados = 0;
        string ultimoAlvoEncontrado = "";

        try
        {
            using (var reader = new StreamReader(fileName))
            using (var csv = new CsvReader(reader,
                                        new CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = ";" }))
            // InvariantCulture não identifica ";" usado no CSV, enquanto que Cultura BR
            // não identifica "." decimal, portanto altero para invariante MAS
            // especifico o delimitador
            {
                // Leitura do cabeçalho exige esses dois passos
                csv.Read(); csv.ReadHeader();
                // Inicio cronômetro antes do loop
                var watch = new System.Diagnostics.Stopwatch();
                watch.Start();
                while (csv.Read())
                {
                    // Leio próximo CNPJ
                    string cnpj = csv.GetField(HEADER_Cnpj);
                    if (!buscar.Contains(cnpj) && contaAlvosEncontrados < buscar.Count)
                    {
                        // Caso não seja buscado, mas ainda não tenha encontrado todos, continue para proxima iteração do loop 
                        // ATENÇÃO: se eu conseguir saber à priori em que ano e mês começam as cotas para os CNPJ em busca, posso evitar
                        // que fique procurando desnecessariamente até o final de arquivos onde o fundo não existe. Para implementar isso,
                        // precisa de um tratamento à parte dos dados.
                        continue;
                    }
                    else if (!buscar.Contains(cnpj) && contaAlvosEncontrados >= buscar.Count)
                    {
                        // Caso não seja buscado, mas já tenha encontrado todos da lista, quebrar loop (desnecessário continuar percorrendo arquivo)
                        break;
                    }
                    else
                    {
                        // Se cheguei aqui, estou na linha de um CNPJ buscado.
                        // Só vou incrementar o contador de alvos encontrados se o CNPJ dessa linha
                        // for diferente do último alvo encontrado, ou seja, "encontrei novo alvo"
                        if (cnpj != ultimoAlvoEncontrado)
                        {
                            contaAlvosEncontrados++;
                            ultimoAlvoEncontrado = cnpj;
                        }
                        // Prossigo lendo demais campos, crio o registro e armazeno na lista de resultados
                        var registro = new RegistroCotas
                        (
                            Cnpj: cnpj,
                            Data: csv.GetField<DateOnly>(HEADER_Data),
                            Cota: csv.GetField<double>(HEADER_Cota),
                            NumCotistas: csv.GetField<int>(HEADER_NumCotistas)
                        );
                        registros.Add(registro);
                    }
                }
                watch.Stop();
                Console.WriteLine($"concluído, tempo: {watch.Elapsed}, encontrados {contaAlvosEncontrados} de {buscar.Count} CNPJs");
            }
        }
        catch (System.Exception e)
        {
            Console.WriteLine($"ERRO ({e.Message}). Arquivo ignorado.");
        }
        
    }

    private void ParseAnoMesComCache(int ano, int mes, List<string> buscar, List<RegistroCotas> registros)
    {
        // Garantir que cache de presenças esteja carregado
        if (_cachePresencas==null) LerCacheDePresencasDeArquivo();
            
        // Ideia: criar uma nova lista de cnpjs a buscar que contenha apenas os CNPJs
        // presentes neste mês e passá-la como argumento apra a função ParseAnoMes
        List<string> presentes = new();

        // Confrontar cada CNPJ da lista de busca com o cache de presenças
        foreach (var cnpj in _buscar)
        {
            // Procuro cnpj no cache de presenças
            RegistroPresenca? rp = _cachePresencas?.SingleOrDefault(x => x.Cnpj == cnpj);
            if (rp == null)
            {
                // cnpj não localizado no cache: abortar 
                throw new Exception($"CNPJ {cnpj} não localizado no cache. Reconstruir cache ou executar sem.");
            }
            else
            {
                // cnpj localizado no cache. Incluir na lista de "presentes em cache"
                if (ConfirmarPresencaNoPeriodo(rp, ano, mes))
                {
                    presentes.Add(cnpj);
                }
            }
        }

        // Conclusão: prosseguir com o parse apenas dos CNPJs que sei que constarão no ano e mês em questão
        Console.Write($"> [Cache ativo]: ");
        ParseAnoMes(ano, mes, presentes, registros);        

        // Auxiliar
        static bool ConfirmarPresencaNoPeriodo(RegistroPresenca rp, int ano, int mes) => rp switch
        {
            RegistroPresenca r when r.Ano < ano => true,           // cnpj presente já em ano anterior ao pesquisado
            RegistroPresenca r when r.Ano > ano => false,          // cnpj presente somente em ano posterior ao pesquisado
            RegistroPresenca r when r.Ano == ano => r.Mes <= mes,  // pesquisando cnpj em ano registrado, verificar mês
            _ => throw new Exception($"Erro em {nameof(ConfirmarPresencaNoPeriodo)}")
        };
    }

    private void EscreverNovoArquivo()
    {
        // Tratamento: se não foi especificado path de escrita, usar mesmo de leitura
        string path;
        if (_opts.PathEscrita=="")
        {
            path = _opts.PathLeitura;
        }
        else
        {
            path= _opts.PathEscrita;
        }
        using (var writer = new StreamWriter($@"{path}{_opts.NomeArquivoFinal}.csv"))// CUIDADO, \
        using (var csv = new CsvWriter(writer, CultureInfo.GetCultureInfo("pt-BR")))  // pt-BR para melhor tratamento no Excel
        {
            csv.WriteRecords(_cotas);
        }                   
    }

    
    // Funções auxiliares
    public static void Usage()
    {
        Console.WriteLine(@" Forma de uso:");
        Console.WriteLine(@" ");
        Console.WriteLine(@" FundosParser < c:\caminho\cnpjs.txt -op1=OPCAO1 -op2=OPCAO2 ... -cmd=[COMANDO] ");
        Console.WriteLine(@" ");
        Console.WriteLine(@" ");
        Console.WriteLine(@" * Arquivo de CNPJs dos fundos de interesse, texto simples. Deve");
        Console.WriteLine(@"   constar um CNPJ em cada linha no formato 00.000.000/0000-00");
        Console.WriteLine(@" ");
        Console.WriteLine(@" ");
        Console.WriteLine(@" * Opções op1, op2, etc:");
        Console.WriteLine(@"   -anoi, -mesi: Ano inicial, mes inicial (padrão: Janeiro de 2017)");
        Console.WriteLine(@"   -anof, -mesf: Ano final, mes final (padrão: data de hoje)");
        Console.WriteLine(@"   -in:    Pasta de leitura dos arquivos .csv (dados originais) e");
        Console.WriteLine(@"           também pasta de leitura e escrita do cache de presenças");
        Console.WriteLine(@"           usar -in=. para trabalhar na pasta atual");
        Console.WriteLine(@"           (padrão: c:\FundosParser)");
        Console.WriteLine(@"   -out:   Pasta de escrita do arquivo filtrado resultante");
        Console.WriteLine(@"           (padrão: escreve na mesma pasta de leitura)");
        Console.WriteLine(@"   -nome:  Nome do arquivo filtrado (padrão _DADOS_FILTRADOS)");
        Console.WriteLine(@"   -cache: Nome do arquivo cache de presenças (padrão: _cache)");
        Console.WriteLine(@" ");
        Console.WriteLine(@" ");
        Console.WriteLine(@" * Comandos (Obrigatório):");
        Console.WriteLine(@"   -cmd=Help: Imprime essa mensagem");
        Console.WriteLine(@"   -cmd=Parametros: Apenas mostra os parâmetros, para conferência");
        Console.WriteLine(@"   -cmd=Cache: Constrói cache de presenças. Exige parâmetro -cache");
        Console.WriteLine(@"   -cmd=MostrarCache: Lê o cache salvo e mostra. Exige parâmetro -cache");
        Console.WriteLine(@"   -cmd=Processar: Executa processamento e escreve arquivo filtrado");
        Console.WriteLine(@"        O processamento fará uso do cache se arquivo for informado ");
        Console.WriteLine(@"        com parÂmetro -cache=nome");
        Console.WriteLine(@" ");
        Console.WriteLine(@" ");
        Console.WriteLine(@" * Sobre o Cache: recurso em desenvolvimento, ganho de tempo mínimo"); 
        Console.WriteLine(@"   é seguro não usar.");
        Console.WriteLine(@" "); 
        Console.WriteLine(@" * Sobre os arquivos de dados .csv: são arquivos mensais disponibizados");
        Console.WriteLine(@"   pela CVM que devem ser baixados e colocados na pasta especificada com");
        Console.WriteLine(@"   o parâmetro -in. Em março de 2022, arquivos podem ser baixados de");
        Console.WriteLine(@"   http://dados.cvm.gov.br/dados/FI/DOC/INF_DIARIO/DADOS/");
        Console.WriteLine(@" ");
        Console.WriteLine(@" ");
    }    

    public void MostrarParametros()
    {
        Console.WriteLine($"> Parâmetros:");
        Console.WriteLine($">   Ano-Mes-Inicial:    {_opts.AnoInicial:0000}-{_opts.MesInicial:00}");
        Console.WriteLine($">   Ano-Mes-Final:      {_opts.AnoFinal:0000}-{_opts.MesFinal:00}");
        Console.WriteLine($">   Pasta de leitura:   {_opts.PathLeitura}");
        Console.WriteLine($">   Pasta de escrita:   {_opts.PathEscrita}");
        Console.WriteLine($">   Arq. .csv de saída: {_opts.NomeArquivoFinal}");
        Console.WriteLine($">   Arq. .csv de cache: {_opts.NomeArquivoCacheDePresencas}");
    }

    public void MostrarParametrosBrutos()
    {
        // Converte diretamente objeto de opções
        Console.Write($"> ");
        Console.WriteLine(_opts);
    }

    public void MostrarRegistrosCnpj(string cnpj)
    {
        Console.WriteLine();
        Console.WriteLine($"REGISTROS DO CNPJ {cnpj}:");
        foreach (var item in _cotas.Where(r => r.Cnpj.Equals(cnpj)))
            Console.WriteLine(item);
    }

}
