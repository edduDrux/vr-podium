namespace Podium.Api
{
    /// <summary>
    /// Espelha PresentationStatus do backend (app/core/enums.py). São constantes de
    /// string, e não um enum C#: o valor vem do JSON, e um enum daria a ilusão de que o
    /// compilador garante o casamento — não garante. Se o backend ganhar um estado novo,
    /// o cliente precisa cair no `default` do switch, não num valor inventado.
    /// </summary>
    public static class SessionStatus
    {
        public const string Created = "created";
        public const string AudioReceived = "audio_received";
        public const string Processing = "processing";
        public const string Completed = "completed";
        public const string Failed = "failed";
    }

    /// <summary>Espelha PersonaType do backend. Três, não quatro (migration f4b1c9d2e370).</summary>
    public static class PersonaValues
    {
        public const string ProfessorRigoroso = "professor_rigoroso";
        public const string OrientadorAcolhedor = "orientador_acolhedor";
        public const string EspecialistaTecnico = "especialista_tecnico";
    }

    /// <summary>Espelha ScenarioType do backend. O MVP entrega apenas a sala de aula.</summary>
    public static class ScenarioValues
    {
        public const string SalaDeAula = "sala_de_aula";
    }
}
