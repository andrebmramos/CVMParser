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
    private readonly List<string> _cnpjs;

    // Dados usados pela funcionalidade antiga (uma saída, em vetor de registros)
    private List<RegistroCotas>     _cotasRegistros = new();      // informação principal das cotas. Obtenção em Parse
    private List<RegistroPresenca>? _cachePresencas; // cache das datas quando cada fundo de interesse se faz presente
    
    // Dados usados pela nova funcionalidade (dupla saída, cotas e variações em "tabelonas")
    private double[,] _cotas;               // informações de cotas [índice_data, índice_cnpj]
    private bool[] _flagTemDados;           // flag se aquela linha (data!) em particular tem valores (dia útil)
    private DateOnly _dataInicialMatriz;    // data inicial para matriz de dados (*)
    private DateOnly _dataFinalMatriz;      // data inicial para matriz de dados (*)
    private int linhasMatriz;               // quantidade de linhas é o número de datas corridas (não só dias úteils!) (*)
                                            // (*) A data inicial é um dia primeiro, a data final é o último dia do mês
                                            //     independentemente das datas especificadas como parâmetro para o Parse
                                            //     Notar também que qualquer data pode ser identificada pelo número da linha
                                            //     sabendo-se a data inicial

    public ParserCore(ParserOptions opts, List<string> buscar)
    {
        _opts=opts;
        _cnpjs=buscar;
        _cnpjs.Sort();

        // Criação da matrix de cotas[data,cnpj]
        // Datas: todas desde (AnoInicial, MesInicial, dia 1) 
        //        e (AnoFinal,  MesFinal,  "Último Dia", a definir abaixo)
        // Várias datas serão fds e feriado, e ficarão sem registro, ou seja,
        // há desperdício de memória pela praticidade. Tratar depois!
        _dataInicialMatriz = new DateOnly(opts.AnoInicial, opts.MesInicial, 1);
        int ultimoDia = opts.MesFinal switch
        {
            1 or 3 or 5 or 7 or 8 or 10 or 12 => 31,         // meses de 31 dias
            4 or 6 or 9 or 11 => 30,                         // meses de 30 dias
            2 when DateTime.IsLeapYear(opts.AnoFinal) => 29, // fevereiro bissexto           
            _ => 28                                          // else, fevereiro não bissexto
        };
        _dataFinalMatriz = new DateOnly(opts.AnoFinal, opts.MesFinal, ultimoDia);
        linhasMatriz = DiasContagem(_dataInicialMatriz, _dataFinalMatriz);
        _cotas = new double[linhasMatriz, _cnpjs.Count];
        _flagTemDados = new bool[linhasMatriz];
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
            case Comando.ParseSimples: // Processa arquivos da CVM e escreve arquivo filtrado em forma de vetores = ANTIGO!!!!
                ParsePeriodo();
                if (_opts.EscreverSaida)
                {
                    System.Console.WriteLine("- Escrevendo saída em forma de vetores");
                    EscreverNovoArquivo();                    
                }
                else
                {
                    Console.WriteLine("- Ignorando escrita do arquivo de saída");
                }
                break;
            case Comando.ParseDuplo:   // Processa arquivos da CVM e escreve 2 arquivos filtrados com cotas e variações
                ParsePeriodoTabelona();
                if (_opts.EscreverSaida)
                {
                    System.Console.WriteLine("- Escrevendo saída em dupla tabela");
                    //var watch = new System.Diagnostics.Stopwatch();
                    //watch.Start();
                    EscreverTabelonas();
                    //watch.Stop();
                    //Console.WriteLine($"...demorou {watch.Elapsed}");                    
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
            case Comando.Teste:
                Console.WriteLine("Comando para teste de novas funcionalidades. No momento, sem implementação");
                break;
            default:
                throw new ArgumentException($"### Comando {_opts.Cmd} não implementado.");
        }
    }
    

    
    // Funções relativas ao cache de presenças
    private void ConstruirESalvarCacheDePresencas()
    {
        Console.WriteLine($"> Construindo cache de presenças, buscando {_cnpjs.Count} fundos");
        
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
                string fileName = $@"{_opts.PathLeitura}inf_diario_fi_{ano:0000}{mes:00}.csv"; 
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
                        if (cnpj != cnpj_anterior && _cnpjs.Contains(cnpj))
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
                    if (contaNovosTotal>=_cnpjs.Count)
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
        using (var writer = new StreamWriter($@"{_opts.PathLeitura}{_opts.NomeArquivoCacheDePresencas}.csv"))
        using (var csv = new CsvWriter(writer, CultureInfo.GetCultureInfo("pt-BR")))  // pt-BR para melhor tratamento no Excel
        {
            csv.WriteRecords(_cachePresencas);
        }
    }

    private void LerCacheDePresencasDeArquivo()
    {       
        using (var reader = new StreamReader($@"{_opts.PathLeitura}{_opts.NomeArquivoCacheDePresencas}.csv"))
        using (var csv = new CsvReader(reader,
                                       new CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = ";" }))
            _cachePresencas = csv.GetRecords<RegistroPresenca>().ToList();
    }


    // Funções principais DA FUNCIONALIDADE ANTIGA
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
                    ParseAnoMesComCache(ano, mes, _cnpjs, _cotasRegistros);
                }
                else 
                {
                    ParseAnoMes(ano, mes, _cnpjs, _cotasRegistros);
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
        string fileName = $@"{_opts.PathLeitura}inf_diario_fi_{ano:0000}{mes:00}.csv";
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
        foreach (var cnpj in _cnpjs)
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
        using (var writer = new StreamWriter($@"{path}{_opts.NomeArquivoFinal}.csv"))
        using (var csv = new CsvWriter(writer, CultureInfo.GetCultureInfo("pt-BR")))  // pt-BR para melhor tratamento no Excel
        {
            csv.WriteRecords(_cotasRegistros);
        }                   
    }


    // Funções principais, funcionalidade nova, dupla saída com todas as cotas e variações
    private void ParsePeriodoTabelona()
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
                ParseAnoMesTabelona(ano, mes);//, _cnpjs, _cotasRegistros);                
                mesesProcessados++;
            }
        }
        // Fim
        watch.Stop();
        Console.WriteLine($"> Processados {mesesProcessados} arquivos, tempo: {watch.Elapsed}");
    }

    private void ParseAnoMesTabelona(int ano, int mes)
    {
        // Identifico arquivo de dados da CVM do respectivo ano e mês
        string fileName = $@"{_opts.PathLeitura}inf_diario_fi_{ano:0000}{mes:00}.csv";
        Console.Write($"> Processando arquivo {fileName}...");

        // Variáveis auxiliares para tratar quantos CNPJs foram identificados
        int contaAlvosEncontrados = 0;
        string ultimoAlvoEncontrado = "";
        string cnpj = "";
        string cnpjDescartado = "";

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
                    cnpj = csv.GetField(HEADER_Cnpj);
                    
                    if (cnpj==cnpjDescartado) 
                        // Igual ao último CNPJ que, tendo sido consultada lista de busca, não interessa
                    {
                        continue;
                    }
                    else if (!_cnpjs.Contains(cnpj)) 
                        // Consulto novo Cnpj na lista de busca. Se não consta, programo para descarte
                    {
                        cnpjDescartado=cnpj; // para descartar na próxima interação sem consultar lista
                        if (contaAlvosEncontrados < _cnpjs.Count)
                        {
                            // Caso não seja buscado, mas ainda não tenha encontrado todos, continue para proxima iteração do loop 
                            // ATENÇÃO: se eu conseguir saber à priori em que ano e mês começam as cotas para os CNPJ em busca, posso evitar
                            // que fique procurando desnecessariamente até o final de arquivos onde o fundo não existe. Para implementar isso,
                            // precisa de um tratamento à parte dos dados.
                            continue;
                        }
                        else // contaAlvosEncontrados >= _cnpjs.Count
                        {
                            // Caso não seja buscado, mas já tenha encontrado todos da lista, quebrar loop (desnecessário continuar percorrendo arquivo)
                            break;
                        }
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
                        int linha = IndexFromData(csv.GetField<DateOnly>(HEADER_Data));
                        _cotas[linha, _cnpjs.IndexOf(cnpj)] = csv.GetField<double>(HEADER_Cota);
                        _flagTemDados[linha] = true;
                    }
                }
                watch.Stop();
                Console.WriteLine($"concluído, tempo: {watch.Elapsed}, encontrados {contaAlvosEncontrados} de {_cnpjs.Count} CNPJs");
            }
        }
        catch (System.Exception e)
        {
            Console.WriteLine($"ERRO ({e.Message}). Arquivo ignorado.");
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
        using var wcotas = new StreamWriter($@"{path}{_opts.NomeArquivoFinal}_cotas.csv");     
        using var wvardia = new StreamWriter($@"{path}{_opts.NomeArquivoFinal}_vardia.csv");
                
        // Auxiliares
        double[] cotaOntem  = new double[_cnpjs.Count];
        double cotaHoje;

        // Geração da 1a linha (cabeçalho) dos 2 arquivos, onde em ambos se lê "Data" e os CNPJs
        wcotas.Write("Data");
        wvardia.Write("Data");
        foreach (var cnpj in _cnpjs)
        {
            wcotas.Write($";{cnpj}");
            wvardia.Write($";{cnpj}");
        }
        wcotas.Write("\n");
        wvardia.Write("\n");

        // Gera linhas, data por data, com cotas e variações diárias
        for (int i = 0; i<linhasMatriz; i++)
        {
            // Antes de tudo, checar se alguma cota foi inserida na data em questão, senão, desprezar            
            if (_flagTemDados[i]==false)
            {
                continue;
            }
            else
            {
                // Primeiro valor em ambos os arquivos é a própria data, obtida do índice (linha da matriz)
                DateOnly data = DataFromIndex(i);
                wcotas.Write(data);
                wvardia.Write(data);
                // Loop por todos os CNPJs, indexados por i
                for (int j = 0; j<_cnpjs.Count; j++)
                {
                    cotaHoje = _cotas[i, j];
                    wcotas.Write($";{cotaHoje}");
                    if (cotaOntem[j] > 0) // calculo e escrevo variação diária apenas se valor "de ontem" > 0
                    {
                        wvardia.Write($";{cotaHoje/cotaOntem[j]-1}");
                    }
                    else
                    {
                        wvardia.Write(";");
                    }
                    cotaOntem[j] = cotaHoje; // Cota de hoje já foi escrita, já foi usada no cálculo da variação e 
                                             // agora é armazenada no array de cotas de ontem para ser usada no cálculo
                                             // da variação da próxima linha
                }
                // Fim da linha nos 2 arquivos de saída
                wcotas.Write("\n");
                wvardia.Write("\n");
            }            
        }
    }



    // FUNÇÕES AUXILIARES
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
        Console.WriteLine(@"           (padrão: c:\FundosParser\)");
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
        Console.WriteLine(@"   -cmd=ParseSimples: Executa processamento e escreve arquivo filtrado");
        Console.WriteLine(@"        O processamento fará uso do cache se arquivo for informado ");
        Console.WriteLine(@"        com parâmetro -cache=nome");
        Console.WriteLine(@"   -cmd=ParseDuplo: Executa processamento e escreve duas tabelonas");
        Console.WriteLine(@"        com o nome especificado acrescentado de _cotas e _vardia ");
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
        foreach (var item in _cotasRegistros.Where(r => r.Cnpj.Equals(cnpj)))
            Console.WriteLine(item);
    }


    // OUTRAS FUNÇÕES AUXILIARES, referentes ao tratamento de datas com DateOnly
    // Função utilitária p/calcular diferença em dias entre 2 datas tipo DateOnly. Se datas iguais, distânmcia zero
    private int DiasDistancia(DateOnly inicial, DateOnly final)
        => final.DayNumber - inicial.DayNumber;

    // Função utilitária p/calcular contagem de dias entre 2 datas. Se datas iguais, 1 dia. 
    private int DiasContagem(DateOnly inicial, DateOnly final)
        => final.DayNumber - inicial.DayNumber + 1;

    // O índice das datas, ou seja, linha da data na matriz _cotas[data,cnpj]
    private int IndexFromData(DateOnly data)
        => DiasDistancia(_dataInicialMatriz, data);

    // A data correspondente a um índice, ou seja, dataInicial somada ao próprio índice como número de dias
    private DateOnly DataFromIndex(int i)
        => _dataInicialMatriz.AddDays(i);

}
