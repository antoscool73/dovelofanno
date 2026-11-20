-- 20/11/2026: la pagina principale si chiama «La mia lista» (prima «La mia serata») e così la lista fissa di ogni profilo.
UPDATE lists SET name = 'La mia lista' WHERE is_tonight = 1 AND name = 'La mia serata in TV';
