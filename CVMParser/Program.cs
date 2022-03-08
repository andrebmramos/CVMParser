using FundosParser.Core;
using FundosParser.Modelos;
using Microsoft.Extensions.Configuration; // Requer: Install-Package Microsoft.Extensions.Hosting



namespace FundosParser;


public class Program
{

    // Estruturas principais:
    private static List<string> _buscar = new();    // Lista de CNPJs a serem buscados
    

    static void Main(string[] args)
    {
        // Preparo configurador que lerá os parâmetros de linha de comando
        IConfiguration config = new ConfigurationBuilder()
            .AddCommandLine(args, ParserOptions.switchMappings)
            .Build();

        // Carga dos parâmetros feita pelo configurador
        ParserOptions opts = config.Get<ParserOptions>();


        // Leitura do arquivo de CNPJs
        // Atenção: espera-se SEMPRE receber um arquivo com CNPJs da forma
        // c:\> FundosParser < c:\temp\cnpj.txt
        // onde cnpj.txt = arquivo com um cnpj por linha
        string? cnpj = Console.ReadLine()?.Trim();
        while (!String.IsNullOrEmpty(cnpj))
        {
            Console.WriteLine($"- Solicitado {cnpj}");
            _buscar.Add(cnpj);
            cnpj = Console.ReadLine()?.Trim();
        }
        if (_buscar.Count < 1)
        {            
            throw new Exception("ERRO: nenhum CNPJ encontrado");
        }


        // Criação do Parser
        ParserCore fp = new(opts, _buscar);
        fp.MostrarParametros();
        fp.MostrarParametrosResumidos();

        // Criação e teste do cache de presenças
        //fp.ConstruirCacheDePresencas();
        //fp.MostrarCacheDePresencas();
                
        // Teste principal, com e sem uso do cache de presenças
        // fp.ParsePeriodo(false);
        fp.ParsePeriodo();
        //fp.Processar();

        


    }

}


