namespace DiffForge.Core.Compare;

/// <summary>
/// Puntaje de parecido entre dos líneas (ya normalizadas por las reglas de importancia).
/// Lo usa <see cref="ClosenessMatcher"/> para decidir qué línea borrada se empareja con qué
/// línea insertada dentro de un bloque de cambios. Se emparejan solo si el puntaje es
/// mayor o igual a <see cref="ClosenessMatcher.Threshold"/>.
/// </summary>
public static class LineSimilarity
{
    /// <returns>0 = nada en común, 1 = idénticas.</returns>
    public static double Score(string a, string b)
    {
        // TODO(Allan): implementar el puntaje de similitud.
        // Se llama hasta (líneas borradas × líneas insertadas) veces por bloque, así que debe ser
        // barato: idealmente O(largo de la línea). Los tests de LineSimilarityTests y
        // ClosenessMatchingTests definen el comportamiento esperado.
        // Mientras tanto devuelve un valor constante en el umbral: todo se empareja por posición.
        return a == b ? 1.0 : ClosenessMatcher.Threshold;
    }
}
